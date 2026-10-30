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
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Input for <see cref="CompactConversationActivity"/>.
/// </summary>
/// <param name="ConversationId">The stored conversation to compact.</param>
/// <param name="SummaryId">The summary's idempotency key, derived from the run's turn id.</param>
/// <param name="AgentName">The agent whose compaction settings apply.</param>
/// <param name="ChatClientKey">The agent's chat client key, or <see langword="null"/>.</param>
/// <param name="Incarnation">The conversation incarnation the run used, so a recreated one is left alone.</param>
internal sealed record CompactConversationInput(
    string ConversationId,
    string SummaryId,
    string AgentName,
    string? ChatClientKey,
    string? Incarnation);

/// <summary>
/// Output of <see cref="CompactConversationActivity"/>.
/// </summary>
internal sealed record CompactConversationOutput
{
    /// <summary>Whether a summary was stored.</summary>
    public bool Compacted { get; init; }

    /// <summary>The sequence below which the stored summary replaces the conversation, when one was stored.</summary>
    public long? CoversUpToSequence { get; init; }

    /// <summary>Why compaction failed, when it did. Compaction is best effort, so the run still succeeds.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Summarizes the older turns of a stored conversation and appends the summary to it
/// (<c>WithConversationCompaction</c>). It runs after a run's commit, only when the run's first LLM call found the
/// history past the trigger, and never fails the run: errors are logged and returned.
/// </summary>
/// <remarks>
/// The summary call is made once and its result is stored, so later runs, retries and replays reuse it. The turns
/// it replaces stay stored, and loads pinned before the summary are unchanged.
/// </remarks>
internal sealed partial class CompactConversationActivity(
    ChatClientRegistry chatClientRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<CompactConversationActivity> logger,
    IEnumerable<ConversationCompactionRegistration>? compactions = null,
    IEnumerable<LlmTimeoutRegistration>? llmTimeouts = null,
    IConversationStore? conversationStore = null) : WorkflowActivity<CompactConversationInput, CompactConversationOutput>
{
    /// <summary>The text that introduces a stored summary to the model.</summary>
    internal const string SummaryHeader = "Summary of the conversation before this point:";

    /// <inheritdoc />
    public override async Task<CompactConversationOutput> RunAsync(WorkflowActivityContext context, CompactConversationInput input)
    {
        try
        {
            return await CompactAsync(input).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCompactionFailed(ex, input.ConversationId, input.AgentName);
            return new CompactConversationOutput { Error = ex.Message };
        }
    }

    private async Task<CompactConversationOutput> CompactAsync(CompactConversationInput input)
    {
        if (ConversationCompactionRegistration.Resolve(compactions ?? [], input.AgentName) is not { } registration)
        {
            return new CompactConversationOutput();
        }

        var options = registration.Options;
        var store = ConversationStoreGuard.Require(conversationStore, input.ConversationId);
        var snapshot = await store.LoadAsync(input.ConversationId, incarnation: input.Incarnation).ConfigureAwait(false);
        if (!options.IsDue(TokenEstimate.Of(snapshot.Entries.Select(e => e.Message))))
        {
            return new CompactConversationOutput();
        }

        var previous = snapshot.Entries.FirstOrDefault(e => e.SummaryOf is not null);
        var turns = CompactionPlanner.SplitTurnsByTurnId(snapshot.Entries.Where(e => e.SummaryOf is null));
        var plan = CompactionPlanner.PlanCompaction(turns, previous, options);
        if (plan is null)
        {
            return new CompactConversationOutput();
        }

        var (older, coversUpTo) = plan.Value;
        var text = await SummarizeAsync(input, options, previous, older).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CompactConversationOutput { Error = "The summary came back empty." };
        }

        await store.AppendSummaryAsync(new ConversationSummary(
            input.ConversationId,
            input.SummaryId,
            coversUpTo,
            input.AgentName,
            input.ChatClientKey,
            new WorkflowChatMessage { Role = "system", Content = $"{SummaryHeader}\n{text.Trim()}" },
            snapshot.Incarnation)).ConfigureAwait(false);

        LogCompacted(input.ConversationId, older.Count, coversUpTo);
        return new CompactConversationOutput { Compacted = true, CoversUpToSequence = coversUpTo };
    }

    private async Task<string?> SummarizeAsync(
        CompactConversationInput input,
        ConversationCompactionOptions options,
        ConversationEntry? previous,
        List<ConversationEntry> older)
    {
        // The agent's own client gets the agent's options (its model id, for instance), minus anything that
        // would turn the summary into a tool call or a structured reply.
        ChatOptions? chatOptions = null;
        IChatClient client;
        if (options.ChatClient is { } custom)
        {
            client = custom;
        }
        else
        {
            var config = ResolveAgentConfig(input);
            client = config.ChatClient;
            if (config.BaseOptions?.Clone() is { } agentOptions)
            {
                agentOptions.Tools = null;
                agentOptions.ToolMode = null;
                agentOptions.ResponseFormat = null;
                chatOptions = agentOptions;
            }
        }

        var transcript = CompactionPlanner.BuildTranscript(previous, older, options);

        var timeout = LlmTimeoutRegistration.Resolve(llmTimeouts ?? [], input.AgentName);
        using var timeoutCts = LlmTimeoutRegistration.StartTimeout(timeout);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, options.SummarizationPrompt), new ChatMessage(ChatRole.User, transcript)],
            chatOptions,
            timeoutCts.Token).ConfigureAwait(false);

        return response.Text;
    }

    private ChatClientRegistry.AgentChatConfig ResolveAgentConfig(CompactConversationInput input)
    {
        return AgentChatConfigLookup.Find(chatClientRegistry, agentRegistry, serviceProvider, input.AgentName, input.ChatClientKey)
            ?? throw new InvalidOperationException(
                $"Agent '{input.AgentName}' has no chat client to summarize with. Set ConversationCompactionOptions.ChatClient.");
    }

    [LoggerMessage(LogLevel.Information, "Compacted conversation '{ConversationId}': {Count} messages before entry {CoversUpTo} are now summarized")]
    private partial void LogCompacted(string conversationId, int count, long coversUpTo);

    [LoggerMessage(LogLevel.Warning, "Could not compact conversation '{ConversationId}' for agent '{AgentName}'; the run is unaffected and the next run tries again")]
    private partial void LogCompactionFailed(Exception exception, string conversationId, string agentName);
}
