// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that performs a single LLM call. Each invocation is checkpointed by Dapr Workflows,
/// so on crash recovery the result is replayed without re-executing the LLM call.
/// </summary>
/// <remarks>
/// In addition to the agent's statically-registered instructions/tools (from <see cref="ChatClientRegistry"/>),
/// this activity layers in the per-run contribution computed once by <see cref="ResolveAgentContextActivity"/>
/// (<see cref="CallLlmInput.AdditionalInstructions"/>, <see cref="CallLlmInput.AdditionalMessages"/>,
/// <see cref="CallLlmInput.AdditionalToolNames"/>) so that context providers — e.g. an MAF
/// <c>AgentSkillsProvider</c> — stay in effect for every LLM call within a single agent run.
/// When the run continues a stored conversation (<see cref="CallLlmInput.ConversationId"/>), its history is
/// loaded from the <see cref="IConversationStore"/> on every call, pinned to the prefix the run's first call saw.
/// </remarks>
internal sealed partial class CallLlmActivity(
    ChatClientRegistry chatClientRegistry,
    ToolRegistry toolRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<CallLlmActivity> logger,
    IEnumerable<LlmTimeoutRegistration>? llmTimeouts = null,
    IEnumerable<ReasoningEffortRegistration>? reasoningEfforts = null,
    IEnumerable<ReasoningReplayRegistration>? reasoningReplays = null,
    IConversationStore? conversationStore = null,
    IEnumerable<ContextWindowRegistration>? contextWindows = null,
    IEnumerable<ConversationCompactionRegistration>? compactions = null) : WorkflowActivity<CallLlmInput, CallLlmOutput>
{

    /// <inheritdoc />
    public override async Task<CallLlmOutput> RunAsync(WorkflowActivityContext context, CallLlmInput input)
    {
        AgentTelemetryBaggage.SetAgent(
            input.AgentName,
            input.ChatClientKey,
            AgentTelemetryBaggage.LlmOperation,
            input.TelemetryBaggage);

        var config = AgentChatConfigLookup.Find(chatClientRegistry, agentRegistry, serviceProvider, input.AgentName, input.ChatClientKey)
            ?? throw new InvalidOperationException(
                $"Agent '{input.AgentName}' did not register a chat client configuration. " +
                "Ensure the agent is a ChatClientAgent created via AsAIAgent() or registered " +
                "with explicit instructions and tools via the DaprAgentsBuilderExtensions overloads.");

        // Stored history goes where PriorMessages would: before the run's own messages.
        var history = await LoadHistoryAsync(input).ConfigureAwait(false);
        var runMessages = input.Messages;
        var runMessagesStart = input.RunMessagesStart ?? 0;
        if (history is { Entries.Count: > 0 })
        {
            runMessages = [.. history.Entries.Select(e => e.Message), .. input.Messages];
            runMessagesStart += history.Entries.Count;
        }

        LogLlmCallInfo(input.AgentName, runMessages.Count);

        var timeout = LlmTimeoutRegistration.Resolve(llmTimeouts ?? [], input.AgentName);
        using var timeoutCts = LlmTimeoutRegistration.StartTimeout(timeout);

        try
        {
            var reasoningSource = WorkflowChatMessageConverter.GetReasoningSource(input.AgentName, input.ChatClientKey);
            var replay = ReasoningReplayRegistration.Resolve(reasoningReplays ?? [], input.AgentName);
            var layout = LlmRequestBuilder.BuildChatMessages(config.Instructions, input.AdditionalInstructions, input.AdditionalMessages,
                runMessages, reasoningSource, replay, runMessagesStart,
                historyStartsWithSummary: history is { Entries: [{ SummaryOf: not null }, ..] });
            var messages = FitContextWindow(input.AgentName, layout);
            var additionalTools = ResolveAdditionalTools(input.AgentName, input.AdditionalToolNames);
            var reasoning = ReasoningEffortRegistration.Resolve(reasoningEfforts ?? [], input.AgentName, config.BaseOptions?.Reasoning);
            WarnIfEffortIgnored(input.AgentName, config.ChatClient, reasoning);
            var options = LlmRequestBuilder.BuildChatOptions(config.BaseOptions, config.Tools, additionalTools, input.Options, reasoning,
                isFollowUpCall: input.Iteration > 0);

            var response = await config.ChatClient.GetResponseAsync(messages, options, timeoutCts.Token)
                .ConfigureAwait(false);

            return LlmRequestBuilder.ParseResponse(response, reasoningSource) with
            {
                ReasoningReplay = replay,
                HistorySequence = history?.NextSequence,
                HistoryIncarnation = history?.Incarnation,
                CompactionDue = IsCompactionDue(input.AgentName, history) ? true : null
            };
        }
        // A cancelled gRPC call surfaces as RpcException(Cancelled), not OperationCanceledException,
        // so the token's own state is what tells a timeout apart from any other failure.
        catch (Exception ex) when (timeoutCts.IsCancellationRequested)
        {
            LogLlmCallTimeout(input.AgentName, timeout);
            throw new TimeoutException(
                $"The LLM call for agent '{input.AgentName}' did not complete within {timeout}. " +
                "Raise it with WithLlmTimeout(...) on the agents builder.", ex);
        }
        // Already logged where it is thrown.
        catch (Exception ex) when (ex is not ContextWindowExceededException)
        {
            LogLlmCallError(input.AgentName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Loads the run's stored conversation history, or returns <see langword="null"/> when the run has none.
    /// Reasoning in it is sent back only to the agent and chat client that produced it, through each block's
    /// source, as for <see cref="CallLlmInput.Messages"/>.
    /// </summary>
    private async Task<ConversationSnapshot?> LoadHistoryAsync(CallLlmInput input)
    {
        if (input.ConversationId is null)
        {
            return null;
        }

        var store = ConversationStoreGuard.Require(conversationStore, input.ConversationId);
        return await store.LoadAsync(input.ConversationId, input.HistoryUpToSequence, input.HistoryIncarnation)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The messages to send within the agent's context window (<c>WithContextWindow</c>), or all of them when it has
    /// none. Only what is sent changes; nothing stored does. See <see cref="ContextWindowFitter"/> for what is left out.
    /// When what is always sent does not fit, the call fails instead of reaching the model, which would reject the
    /// request or silently cut it.
    /// </summary>
    private List<ChatMessage> FitContextWindow(string agentName, PromptLayout layout)
    {
        if (ContextWindowRegistration.Resolve(contextWindows ?? [], agentName) is not { } window)
        {
            return layout.Messages;
        }

        var fit = ContextWindowFitter.Fit(layout, window.MaxContextTokens, window.MaxOutputTokens);
        if (fit.Messages is null)
        {
            LogContextWindowExceeded(agentName, fit.RequiredTokens, window.MaxContextTokens, window.MaxOutputTokens);
            throw new ContextWindowExceededException(
                $"The LLM call for agent '{agentName}' needs about {fit.RequiredTokens} tokens for its instructions, context " +
                $"and the run's own messages, more than its context window of {window.MaxContextTokens} tokens leaves " +
                $"after the {window.MaxOutputTokens} kept for the reply, even with every earlier turn left out. Raise " +
                "it with WithContextWindow(...) if the model allows more, or shorten what the run's tools return.");
        }

        if (fit.Dropped > 0)
        {
            LogContextWindowApplied(agentName, layout.Messages.Count, fit.Messages.Count);
        }

        return fit.Messages;
    }

    /// <summary>
    /// Whether the run's stored history has grown past the agent's compaction trigger. Recorded in the output, so
    /// the workflow schedules the compaction the same way on every replay.
    /// </summary>
    private bool IsCompactionDue(string agentName, ConversationSnapshot? history) =>
        history is { Entries.Count: > 0 }
        && ConversationCompactionRegistration.Resolve(compactions ?? [], agentName) is { } compaction
        && compaction.Options.IsDue(TokenEstimate.Of(history.Entries.Select(e => e.Message)));

    /// <summary>
    /// Resolves tool names contributed by <see cref="ResolveAgentContextActivity"/> (e.g. a skills
    /// provider's <c>load_skill</c>/<c>read_skill_resource</c>/<c>run_skill_script</c>) back into
    /// <see cref="AITool"/> instances via <see cref="ToolRegistry"/>, where they were registered.
    /// </summary>
    private IReadOnlyList<AITool>? ResolveAdditionalTools(string agentName, List<string>? toolNames)
    {
        if (toolNames is not { Count: > 0 })
        {
            return null;
        }

        List<AITool>? resolved = null;
        foreach (var name in toolNames)
        {
            var fn = toolRegistry.Get(agentName, name);
            if (fn is null)
            {
                continue;
            }

            (resolved ??= []).Add(fn);
        }

        return resolved;
    }

    /// <summary>
    /// Warns, once per agent, when the agent asks for a reasoning effort its chat client cannot send:
    /// Dapr Conversation (<c>DaprChatClient</c>) has no field for it today.
    /// </summary>
    private void WarnIfEffortIgnored(string agentName, IChatClient chatClient, ReasoningOptions? reasoning)
    {
        if (reasoning?.Effort is null
            || !Hosting.DaprAgentsBuilder.IsDaprChatClient(chatClient)
            || !chatClientRegistry.WarnedIgnoredReasoningEffort.TryAdd(agentName, true))
        {
            return;
        }

        LogReasoningEffortIgnored(agentName);
    }

    [LoggerMessage(LogLevel.Information, "Calling LLM for agent '{AgentName}' with {MessageCount} messages")]
    private partial void LogLlmCallInfo(string agentName, int messageCount);

    [LoggerMessage(LogLevel.Error, "LLM call failed for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogLlmCallError(string agentName, string errorMessage);

    [LoggerMessage(LogLevel.Warning, "Agent '{AgentName}' has a reasoning effort, but its chat client is DaprChatClient, which does not send it: the Dapr Conversation API has no field for it")]
    private partial void LogReasoningEffortIgnored(string agentName);

    [LoggerMessage(LogLevel.Information, "Fitted agent '{AgentName}' messages to its context window: {Before} messages became {After}")]
    private partial void LogContextWindowApplied(string agentName, int before, int after);

    [LoggerMessage(LogLevel.Error, "LLM call for agent '{AgentName}' needs about {Tokens} tokens without any earlier turn, more than its context window of {ContextTokens} less {OutputTokens} for the reply")]
    private partial void LogContextWindowExceeded(string agentName, long tokens, int contextTokens, int outputTokens);

    [LoggerMessage(LogLevel.Error, "LLM call for agent '{AgentName}' timed out after {Timeout}")]
    private partial void LogLlmCallTimeout(string agentName, TimeSpan timeout);
}
