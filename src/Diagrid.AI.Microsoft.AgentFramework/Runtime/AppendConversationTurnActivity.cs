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
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Input for <see cref="AppendConversationTurnActivity"/>: one finished run's messages.
/// </summary>
/// <param name="ConversationId">The stored conversation.</param>
/// <param name="TurnId">The run's turn id, which makes a redelivered append change nothing.</param>
/// <param name="BaseSequence">How many stored entries the run's LLM calls saw.</param>
/// <param name="AgentName">The agent that ran.</param>
/// <param name="ChatClientKey">The agent's chat client key, or <see langword="null"/>.</param>
/// <param name="Messages">The run's messages, as returned in <see cref="AgentRunResult.TurnMessages"/>.</param>
internal sealed record AppendConversationTurnInput(
    string ConversationId,
    string TurnId,
    long BaseSequence,
    string AgentName,
    string? ChatClientKey,
    List<WorkflowChatMessage> Messages)
{
    /// <summary>The incarnation of the history the run saw, or <see langword="null"/> when it was empty.</summary>
    public string? BaseIncarnation { get; init; }
}

/// <summary>
/// Commits a finished agent run's messages to its stored conversation as one turn. This is the only place a
/// run writes to the conversation store, and it runs only after the run produced its final response (or hit
/// its iteration limit), so a failed run never stores anything.
/// </summary>
/// <remarks>
/// It is always registered, even without <c>WithConversationStore(...)</c>, so a run replayed in a process
/// that lacks the store fails with an explanation instead of an unknown activity.
/// </remarks>
internal sealed partial class AppendConversationTurnActivity(
    ILogger<AppendConversationTurnActivity> logger,
    IConversationStore? conversationStore = null) : WorkflowActivity<AppendConversationTurnInput, ConversationAppendResult>
{
    /// <inheritdoc />
    public override async Task<ConversationAppendResult> RunAsync(WorkflowActivityContext context, AppendConversationTurnInput input)
    {
        var store = ConversationStoreGuard.Require(conversationStore, input.ConversationId);

        var result = await store.AppendTurnAsync(new ConversationTurn(
            input.ConversationId,
            input.TurnId,
            input.BaseSequence,
            input.AgentName,
            input.ChatClientKey,
            input.Messages,
            input.BaseIncarnation)).ConfigureAwait(false);

        if (result.BaseMoved)
        {
            LogBaseMoved(input.AgentName, input.ConversationId, input.BaseSequence, result.FirstSequence);
        }

        return result;
    }

    [LoggerMessage(LogLevel.Warning, "Agent '{AgentName}' ran on {BaseSequence} entries of conversation '{ConversationId}', but other turns were committed first; its turn starts at entry {FirstSequence}")]
    private partial void LogBaseMoved(string agentName, string conversationId, long baseSequence, long firstSequence);
}

/// <summary>
/// Deletes a stored conversation, as an activity of the calling workflow.
/// </summary>
/// <remarks>Always registered; see <see cref="AppendConversationTurnActivity"/>.</remarks>
internal sealed class DeleteConversationActivity(IConversationStore? conversationStore = null)
    : WorkflowActivity<string, bool>
{
    /// <inheritdoc />
    public override Task<bool> RunAsync(WorkflowActivityContext context, string conversationId) =>
        ConversationStoreGuard.Require(conversationStore, conversationId).DeleteAsync(conversationId);
}

internal static class ConversationStoreGuard
{
    /// <summary>
    /// The registered store, or an error that says how to register one.
    /// </summary>
    public static IConversationStore Require(IConversationStore? store, string conversationId) =>
        store ?? throw new InvalidOperationException(
            $"The agent run uses conversation '{conversationId}', but no conversation store is registered. " +
            "Call WithConversationStore(...) on the agents builder.");
}
