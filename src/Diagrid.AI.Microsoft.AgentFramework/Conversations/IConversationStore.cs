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

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// Durable, append-only store of agent conversation history, kept outside workflow payloads.
/// Each committed turn is all-or-nothing, so a failed agent run never contributes entries.
/// </summary>
/// <remarks>
/// <c>WithConversationStore(...)</c> registers the Dapr state store implementation. Register your own
/// implementation before calling it to replace that one.
/// </remarks>
/// <remarks>
/// <para>
/// To plug in your own store, register it before <c>WithConversationStore(...)</c>. What the runtime calls:
/// <see cref="LoadAsync"/> and <see cref="AppendTurnAsync"/> on every run with a conversation;
/// <see cref="DeleteAsync"/> from <c>DeleteConversationAsync</c>; <see cref="AppendSummaryAsync"/> only with
/// <c>WithConversationCompaction</c>. <see cref="LoadEntriesAsync"/> is never called by the runtime; it is for your
/// own code (a full transcript). A store for agents without compaction can throw
/// <see cref="NotSupportedException"/> from the members it is not asked for.
/// </para>
/// </remarks>
public interface IConversationStore
{
    /// <summary>
    /// Loads the committed history of a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation identifier chosen by the caller.</param>
    /// <param name="upToSequence">
    /// When set, only entries with a <see cref="ConversationEntry.Sequence"/> below this value are returned, which
    /// is a prefix that later appends never change. <see langword="null"/> returns everything committed so far.
    /// </param>
    /// <param name="incarnation">
    /// When set, the <see cref="ConversationSnapshot.Incarnation"/> of an earlier load. The load fails if the
    /// conversation has since been deleted or expired and created again, instead of returning the new one's
    /// history. <see langword="null"/> skips the check.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The history. A conversation that does not exist gives an empty snapshot whose
    /// <see cref="ConversationSnapshot.NextSequence"/> is 0, when <paramref name="upToSequence"/> is
    /// <see langword="null"/> or 0.
    /// </returns>
    /// <exception cref="ConversationNotFoundException">
    /// The requested prefix no longer exists, because the conversation was deleted or expired.
    /// </exception>
    Task<ConversationSnapshot> LoadAsync(
        string conversationId,
        long? upToSequence = null,
        string? incarnation = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads stored entries by sequence, exactly as committed: every message, including those a summary replaces
    /// in <see cref="LoadAsync"/>, and the summaries themselves (<see cref="ConversationEntry.SummaryOf"/>). Use it
    /// to show or export a full transcript.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="fromSequence">The first sequence to return.</param>
    /// <param name="toSequence">The sequence to stop before, or <see langword="null"/> for the end.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entries in the range, ordered by sequence; empty when the conversation does not exist.</returns>
    Task<IReadOnlyList<ConversationEntry>> LoadEntriesAsync(
        string conversationId, long fromSequence, long? toSequence = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one turn atomically and idempotently. Appending a turn whose <see cref="ConversationTurn.TurnId"/>
    /// is already stored changes nothing and returns <see cref="ConversationAppendStatus.AlreadyAppended"/>.
    /// </summary>
    /// <param name="turn">The turn to append.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Where the turn was stored.</returns>
    /// <exception cref="ConversationNotFoundException">
    /// The history the turn was produced against (<see cref="ConversationTurn.BaseIncarnation"/>) was deleted or expired.
    /// </exception>
    /// <exception cref="ConversationStoreException">The turn could not be committed.</exception>
    Task<ConversationAppendResult> AppendTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a summary of the start of the conversation. Later loads whose prefix includes it return the summary
    /// in place of the entries below <see cref="ConversationSummary.CoversUpToSequence"/>; those entries stay stored.
    /// A load pinned to a prefix from before the summary is unchanged. Appending the same
    /// <see cref="ConversationSummary.SummaryId"/> again changes nothing.
    /// </summary>
    /// <param name="summary">The summary to append.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Where the summary was stored.</returns>
    /// <exception cref="ConversationNotFoundException">
    /// The summarized history (<see cref="ConversationSummary.BaseIncarnation"/>) was deleted or expired.
    /// </exception>
    /// <exception cref="ConversationStoreException">The summary could not be committed.</exception>
    Task<ConversationAppendResult> AppendSummaryAsync(ConversationSummary summary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a conversation and its history. A later append to the same id starts a new conversation.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the conversation existed; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteAsync(string conversationId, CancellationToken cancellationToken = default);
}
