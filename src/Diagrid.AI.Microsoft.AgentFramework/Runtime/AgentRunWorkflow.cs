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
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Durable workflow that orchestrates an agent run. Each LLM call and each tool invocation
/// is scheduled as a separate Dapr Workflow activity so that on crash recovery, already-completed
/// activities are NOT replayed.
/// <list type="number">
///   <item>Resolve the agent's <c>AIContextProviders</c> once for this run (<see cref="ResolveAgentContextActivity"/>)</item>
///   <item>Call the LLM as an activity (<see cref="CallLlmActivity"/>)</item>
///   <item>If the LLM returns tool calls, execute each tool as a separate activity (<see cref="ExecuteToolActivity"/>)</item>
///   <item>Feed tool results back and loop until the LLM returns a final response</item>
///   <item>With a stored conversation, commit the run's messages to it as one turn (<see cref="AppendConversationTurnActivity"/>),
///   then compact it if the first LLM call found it past the agent's trigger (<see cref="CompactConversationActivity"/>)</item>
///   <item>Notify the agent's <c>AIContextProviders</c> that the run has completed (<see cref="CompleteAgentContextActivity"/>)</item>
/// </list>
/// Without a conversation id the activity sequence is exactly what it was before conversations existed, so
/// instances already in flight replay unchanged.
/// </summary>
public sealed class AgentRunWorkflow : Workflow<DaprAgentInvocation, AgentRunResult>
{
    private const int MaxIterations = 20;

    /// <summary>
    /// Retries for the commit to the conversation store. Retrying is always safe: the append is idempotent
    /// per turn id, so a commit that landed before its reply was lost reports itself as already appended.
    /// Errors that cannot succeed on retry (the conversation was deleted mid-run, no store is registered)
    /// are retried too, which delays the run's failure by about 15 seconds; both are rare.
    /// </summary>
    private static readonly WorkflowTaskOptions CommitOptions = new(new WorkflowRetryPolicy(
        maxNumberOfAttempts: 5,
        firstRetryInterval: TimeSpan.FromSeconds(1),
        backoffCoefficient: 2,
        maxRetryInterval: TimeSpan.FromSeconds(30)));

    /// <inheritdoc />
    public override async Task<AgentRunResult> RunAsync(WorkflowContext context, DaprAgentInvocation input)
    {
        if (string.IsNullOrWhiteSpace(input.Message))
        {
            throw new ArgumentException(
                "Message cannot be null or whitespace.", nameof(input));
        }

        // With a stored conversation the history is loaded by each LLM call instead of carried here.
        var conversationId = input.ConversationId;
        if (conversationId is not null && input.PriorMessages is { Count: > 0 })
        {
            throw new ArgumentException(
                "An agent run takes its history either from a stored conversation or from PriorMessages, not both.",
                nameof(input));
        }

        // Only in store mode, so a run without a conversation records no extra history event. The guid keeps
        // the id unique when a purged instance id is reused.
        var turnId = conversationId is not null ? $"{context.InstanceId}:{context.NewGuid():N}" : null;

        // Full message list sent to the LLM (includes prior turns for context)
        var messages = new List<WorkflowChatMessage>();
        if (input.PriorMessages is { Count: > 0 })
        {
            messages.AddRange(input.PriorMessages);
        }

        // Track where this turn's messages start
        var turnStartIndex = messages.Count;

        messages.Add(
            new WorkflowChatMessage { Role = "user", Content = input.Message }
        );

        // The messages that make up "the request" for this run (used by the Invoked phase of the
        // AIContextProvider pipeline for notification purposes). Captured once, before the loop
        // grows `messages` with assistant/tool turns.
        var turnRequestMessages = messages.Skip(turnStartIndex).ToList();

        // Resolve AIContextProviders (e.g. an MAF AgentSkillsProvider) once per run — analogous to
        // what ChatClientAgent.RunAsync does internally before delegating to FunctionInvokingChatClient.
        // A no-op (near-instant) activity call when the agent has no context providers configured.
        // Options/RequestMessages are passed through so providers can establish AIAgent.CurrentRunContext
        // (see AgentRunContextScope) and see this run's AgentRunOptions.AdditionalProperties.
        var agentContext = await context.CallActivityAsync<ResolveAgentContextOutput>(
            nameof(ResolveAgentContextActivity),
            new ResolveAgentContextInput(input.AgentName, input.ChatClientKey, input.TelemetryBaggage)
            {
                RequestMessages = turnRequestMessages,
                Options = input.Options
            });

        // The reasoning replay setting the last LLM call recorded; decides what TurnMessages keep.
        ReasoningReplay? lastReplay = null;

        // The stored history the run's first LLM call saw; every later call loads exactly the same prefix.
        long? historySequence = null;
        string? historyIncarnation = null;
        var compactionDue = false;

        try
        {
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                // Activity: Call the LLM once.
                var llmInput = new CallLlmInput(
                    input.AgentName,
                    input.ChatClientKey,
                    messages,
                    input.Options,
                    input.TelemetryBaggage)
                {
                    AdditionalInstructions = agentContext.Instructions,
                    AdditionalMessages = agentContext.Messages,
                    AdditionalToolNames = agentContext.ToolNames,
                    RunMessagesStart = turnStartIndex,
                    Iteration = iteration,
                    ConversationId = conversationId,
                    HistoryUpToSequence = historySequence,
                    HistoryIncarnation = historyIncarnation
                };
                var llmOutput = await context.CallActivityAsync<CallLlmOutput>(
                    nameof(CallLlmActivity), llmInput);

                if (conversationId is not null && iteration == 0)
                {
                    historySequence = llmOutput.HistorySequence ?? 0;
                    historyIncarnation = llmOutput.HistoryIncarnation;
                    compactionDue = llmOutput.CompactionDue == true;
                }

                // The first output tells the agent's replay setting. When earlier turns' reasoning is
                // never sent back, stop carrying it in the inputs of the run's later calls.
                if (iteration == 0 && !ReasoningReplayPolicy.KeepsReasoningAcrossTurns(llmOutput.ReasoningReplay))
                {
                    StripReasoning(messages, 0, turnStartIndex);
                }

                if (llmOutput.Error is not null)
                {
                    throw new InvalidOperationException(
                        $"LLM call failed for agent '{input.AgentName}': {llmOutput.Error}");
                }

                // If this is a final response (no tool calls), we're done.
                if (llmOutput.IsFinal)
                {
                    // Add the final assistant message
                    messages.Add(new WorkflowChatMessage
                    {
                        Role = "assistant",
                        Content = llmOutput.Text,
                        Reasoning = KeptReasoning(llmOutput)
                    });

                    return await FinishRunAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages,
                        llmOutput.ReasoningReplay, new AgentResponse(
                            WorkflowChatMessageConverter.CreateAssistantMessage(llmOutput.Text, llmOutput.Reasoning)),
                        new StoredRun(turnId, historySequence, historyIncarnation, compactionDue));
                }

                // Tool-call loop
                messages.Add(new WorkflowChatMessage
                {
                    Role = "assistant",
                    Content = llmOutput.Text,
                    FunctionCalls = llmOutput.FunctionCalls,
                    // Kept so the next iteration sends it back: providers such as Anthropic require the
                    // reasoning that preceded a tool call to accompany its result.
                    Reasoning = KeptReasoning(llmOutput)
                });
                lastReplay = llmOutput.ReasoningReplay;

                // Activity: Execute each tool call as a separate activity.
                var toolResults = new List<ExecuteToolOutput>();
                foreach (var fc in llmOutput.FunctionCalls!)
                {
                    var toolInput = new ExecuteToolInput(
                        input.AgentName,
                        fc.Name,
                        fc.CallId,
                        fc.ArgumentsJson,
                        input.TelemetryBaggage);
                    var toolOutput = await context.CallActivityAsync<ExecuteToolOutput>(
                        nameof(ExecuteToolActivity), toolInput);
                    toolResults.Add(toolOutput);
                }

                // Add tool results to the conversation.
                messages.Add(new WorkflowChatMessage
                {
                    Role = "tool",
                    FunctionResults = toolResults.Select(r => new WorkflowFunctionResult
                    {
                        CallId = r.CallId,
                        Name = r.FunctionName,
                        ResultJson = r.ResultJson
                    }).ToList()
                });
            }

            var maxIterationResponse = new AgentResponse(new ChatMessage(ChatRole.Assistant,
                $"Agent '{input.AgentName}' exceeded the maximum of {MaxIterations} iterations without producing a final response."));

            // The same messages TurnMessages returns, ending with tool results: callers that follow up with
            // "stop now" rely on exactly this, so the store keeps it too.
            return await FinishRunAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages,
                lastReplay, maxIterationResponse, new StoredRun(turnId, historySequence, historyIncarnation, compactionDue));
        }
        catch (Exception ex)
        {
            await NotifyAgentContextCompleteAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages, errorMessage: ex.Message);
            throw;
        }
    }

    /// <summary>
    /// What a run against a stored conversation carries to its end: its turn id, the history it was pinned to, and
    /// whether its first LLM call found the conversation due for compaction. All null or false without a conversation.
    /// </summary>
    private readonly record struct StoredRun(string? TurnId, long? HistorySequence, string? HistoryIncarnation, bool CompactionDue);

    /// <summary>
    /// Ends a run that produced its response (a final answer, or the iteration limit): commits its turn, compacts if
    /// due, then notifies the context providers, in that order on every path, and builds the result. One place, so the
    /// activity sequence cannot differ between the two ways a run ends.
    /// </summary>
    private static async Task<AgentRunResult> FinishRunAsync(
        WorkflowContext context,
        DaprAgentInvocation input,
        ResolveAgentContextOutput agentContext,
        List<WorkflowChatMessage> messages,
        int turnStartIndex,
        List<WorkflowChatMessage> turnRequestMessages,
        ReasoningReplay? replay,
        AgentResponse response,
        StoredRun stored)
    {
        var turnMessages = ToTurnMessages(messages, turnStartIndex, replay);
        var commit = await CommitTurnAsync(context, input, stored.TurnId, stored.HistorySequence, stored.HistoryIncarnation, turnMessages);
        await CompactIfDueAsync(context, input, stored.TurnId, stored.CompactionDue, commit, stored.HistoryIncarnation);

        await NotifyAfterCommitAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages, commit);

        return new AgentRunResult
        {
            Response = response,
            TurnMessages = turnMessages,
            ConversationId = input.ConversationId,
            ConversationSequence = EndOf(commit, turnMessages),
            ConversationBaseMoved = commit?.BaseMoved
        };
    }

    /// <summary>
    /// Commits the finished run's messages to its stored conversation, before the context providers are
    /// notified, so they only ever see committed turns. Does nothing, and schedules nothing, without a
    /// conversation. A failed run never gets here, so it stores nothing; once it is here, nothing after the commit
    /// fails the run (see <see cref="NotifyAfterCommitAsync"/>).
    /// </summary>
    /// <remarks>
    /// Cases where a run can still fail with its turn stored, so a caller that retries it as a new run stores the turn
    /// twice: the run is terminated, or its result is too large to return, right after the commit; or the commit lands
    /// but its reply is lost and every retry fails (the store is unreachable for longer than the retries last). All three
    /// need a turn id that stays the same across the caller's retries, which a new run cannot have today.
    /// </remarks>
    private static async Task<ConversationAppendResult?> CommitTurnAsync(
        WorkflowContext context,
        DaprAgentInvocation input,
        string? turnId,
        long? historySequence,
        string? historyIncarnation,
        List<WorkflowChatMessage> turnMessages)
    {
        if (input.ConversationId is null || turnId is null)
        {
            return null;
        }

        return await context.CallActivityAsync<ConversationAppendResult>(
            nameof(AppendConversationTurnActivity),
            new AppendConversationTurnInput(
                input.ConversationId,
                turnId,
                historySequence ?? 0,
                input.AgentName,
                input.ChatClientKey,
                turnMessages)
            {
                BaseIncarnation = historyIncarnation
            },
            CommitOptions);
    }

    /// <summary>
    /// Compacts the stored conversation after the commit when the run's first LLM call found it past the agent's
    /// trigger. Scheduled only then, so runs without compaction keep their activity sequence. The activity never
    /// throws: a failed compaction leaves the run successful and the next run tries again.
    /// </summary>
    private static async Task CompactIfDueAsync(
        WorkflowContext context,
        DaprAgentInvocation input,
        string? turnId,
        bool compactionDue,
        ConversationAppendResult? commit,
        string? historyIncarnation)
    {
        if (!compactionDue || commit is null || input.ConversationId is null || turnId is null)
        {
            return;
        }

        await context.CallActivityAsync<CompactConversationOutput>(
            nameof(CompactConversationActivity),
            new CompactConversationInput(input.ConversationId, $"{turnId}:summary", input.AgentName, input.ChatClientKey,
                historyIncarnation));
    }

    /// <summary>
    /// Where the run's turn ends in the conversation. Not the result's NextSequence: a commit redelivered after
    /// its reply was lost reports the conversation's current length, which may include later turns.
    /// </summary>
    private static long? EndOf(ConversationAppendResult? commit, List<WorkflowChatMessage> turnMessages) =>
        commit is null ? null : commit.FirstSequence + turnMessages.Count;

    /// <summary>
    /// The reasoning to keep on the assistant message: none when the agent's replay setting is
    /// <see cref="ReasoningReplay.Off"/>, so its messages do not grow with reasoning never sent back.
    /// </summary>
    private static List<WorkflowReasoningContent>? KeptReasoning(CallLlmOutput llmOutput) =>
        ReasoningReplayPolicy.KeepsReasoningWithinRun(llmOutput.ReasoningReplay) ? llmOutput.Reasoning : null;

    /// <summary>
    /// The run's own messages returned as <see cref="AgentRunResult.TurnMessages"/>. With
    /// <see cref="ReasoningReplay.WithinRun"/> or <see cref="ReasoningReplay.Off"/> they leave the
    /// reasoning out, so the history passed to later turns does not grow with it.
    /// </summary>
    private static List<WorkflowChatMessage> ToTurnMessages(
        List<WorkflowChatMessage> messages,
        int turnStartIndex,
        ReasoningReplay? replay)
    {
        var turnMessages = messages.Skip(turnStartIndex).ToList();
        if (!ReasoningReplayPolicy.KeepsReasoningAcrossTurns(replay))
        {
            StripReasoning(turnMessages, 0, turnMessages.Count);
        }

        return turnMessages;
    }

    /// <summary>Removes the reasoning from <paramref name="messages"/>[start..end).</summary>
    private static void StripReasoning(List<WorkflowChatMessage> messages, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (messages[i].Reasoning is not null)
            {
                messages[i] = messages[i] with { Reasoning = null };
            }
        }
    }

    /// <summary>
    /// Notifies the context providers that the run succeeded. Once its turn is stored, a failed notification no longer
    /// fails the run: the caller would retry it as a new run, with a new turn id the store cannot recognize, and the
    /// conversation would hold the turn twice. The failure is logged instead. Without a stored turn it fails the run,
    /// as before conversations existed.
    /// </summary>
    private static async Task NotifyAfterCommitAsync(
        WorkflowContext context,
        DaprAgentInvocation input,
        ResolveAgentContextOutput agentContext,
        List<WorkflowChatMessage> messages,
        int turnStartIndex,
        List<WorkflowChatMessage> turnRequestMessages,
        ConversationAppendResult? commit)
    {
        if (commit is null)
        {
            await NotifyAgentContextCompleteAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages, errorMessage: null);
            return;
        }

        try
        {
            await NotifyAgentContextCompleteAsync(context, input, agentContext, messages, turnStartIndex, turnRequestMessages, errorMessage: null);
        }
        catch (WorkflowTaskFailedException ex)
        {
            context.CreateReplaySafeLogger<AgentRunWorkflow>().LogWarning(
                "Agent '{AgentName}' completed and its turn is stored in conversation '{ConversationId}', but notifying its context providers failed: {Error}",
                input.AgentName, input.ConversationId, ex.FailureDetails.ErrorMessage);
        }
    }

    /// <summary>
    /// Notifies the agent's <c>AIContextProviders</c> that this run has completed, successfully or
    /// not (<see cref="CompleteAgentContextActivity"/>). A no-op (near-instant) activity call when
    /// the agent has no context providers configured. Threads through the same <c>Options</c> and
    /// the serialized session <see cref="ResolveAgentContextActivity"/> produced, so
    /// <c>InvokedAsync</c> sees the same invocation metadata and logical session as
    /// <c>InvokingAsync</c> did.
    /// </summary>
    private static Task NotifyAgentContextCompleteAsync(
        WorkflowContext context,
        DaprAgentInvocation input,
        ResolveAgentContextOutput agentContext,
        List<WorkflowChatMessage> messages,
        int turnStartIndex,
        List<WorkflowChatMessage> turnRequestMessages,
        string? errorMessage)
    {
        var responseMessages = messages.Skip(turnStartIndex + turnRequestMessages.Count).ToList();

        return context.CallActivityAsync(
            nameof(CompleteAgentContextActivity),
            new CompleteAgentContextInput(
                input.AgentName,
                input.ChatClientKey,
                turnRequestMessages,
                responseMessages,
                errorMessage,
                input.TelemetryBaggage)
            {
                Options = input.Options,
                SerializedSessionJson = agentContext.SerializedSessionJson
            });
    }
}
