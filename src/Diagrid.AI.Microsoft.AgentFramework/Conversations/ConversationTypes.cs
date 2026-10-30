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

using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// One agent run's messages, committed to a conversation as a unit.
/// </summary>
/// <param name="ConversationId">The conversation the turn belongs to.</param>
/// <param name="TurnId">The idempotency key: unique per run, and the same when the run is replayed.</param>
/// <param name="BaseSequence">
/// The <see cref="ConversationSnapshot.NextSequence"/> of the history the run was produced against.
/// </param>
/// <param name="AgentName">The agent that produced the turn.</param>
/// <param name="ChatClientKey">The chat client key the agent used, or <see langword="null"/>.</param>
/// <param name="Messages">The run's messages in order, starting with the user message.</param>
/// <param name="BaseIncarnation">
/// The <see cref="ConversationSnapshot.Incarnation"/> of the history the run was produced against, or
/// <see langword="null"/> when it was empty. When set, the append fails with
/// <see cref="ConversationNotFoundException"/> if that conversation was deleted or expired since.
/// </param>
public sealed record ConversationTurn(
    string ConversationId,
    string TurnId,
    long BaseSequence,
    string AgentName,
    string? ChatClientKey,
    IReadOnlyList<WorkflowChatMessage> Messages,
    string? BaseIncarnation = null);

/// <summary>
/// A committed message, with its position in the conversation and the turn that produced it.
/// </summary>
/// <param name="Sequence">The message's position in the conversation, starting at 0 and without gaps.</param>
/// <param name="TurnId">The turn that committed the message.</param>
/// <param name="AgentName">The agent that produced the turn.</param>
/// <param name="ChatClientKey">The chat client key the agent used, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When the turn was committed.</param>
/// <param name="Message">The message.</param>
/// <param name="SummaryOf">
/// For a summary (see <see cref="IConversationStore.AppendSummaryAsync"/>), the sequence below which it replaces
/// the conversation; <see langword="null"/> for an ordinary message.
/// </param>
public sealed record ConversationEntry(
    long Sequence,
    string TurnId,
    string AgentName,
    string? ChatClientKey,
    DateTimeOffset CreatedAt,
    WorkflowChatMessage Message,
    long? SummaryOf = null);

/// <summary>
/// A summary of the start of a conversation, which later loads return in place of the entries it covers.
/// </summary>
/// <param name="ConversationId">The conversation.</param>
/// <param name="SummaryId">The idempotency key: appending the same id again changes nothing.</param>
/// <param name="CoversUpToSequence">The summary replaces the entries below this sequence.</param>
/// <param name="AgentName">The agent the summary was made for.</param>
/// <param name="ChatClientKey">That agent's chat client key, or <see langword="null"/>.</param>
/// <param name="Message">The summary message.</param>
/// <param name="BaseIncarnation">
/// The incarnation of the history that was summarized. The append fails with
/// <see cref="ConversationNotFoundException"/> if that conversation was deleted or expired since.
/// </param>
public sealed record ConversationSummary(
    string ConversationId,
    string SummaryId,
    long CoversUpToSequence,
    string AgentName,
    string? ChatClientKey,
    WorkflowChatMessage Message,
    string? BaseIncarnation);

/// <summary>
/// A prefix of a conversation's history.
/// </summary>
/// <param name="NextSequence">
/// The sequence the next appended entry gets. It equals the number of entries only while no summary applies.
/// </param>
/// <param name="Entries">
/// The entries, ordered by <see cref="ConversationEntry.Sequence"/>. When the prefix holds a summary, it comes
/// first and stands for every entry below its <see cref="ConversationEntry.SummaryOf"/>, which are left out, as
/// are older summaries.
/// </param>
/// <param name="Incarnation">
/// An id given to the conversation when it was created, or <see langword="null"/> when it does not exist.
/// It changes when the conversation is deleted or expires and is created again under the same id.
/// </param>
public sealed record ConversationSnapshot(
    long NextSequence,
    IReadOnlyList<ConversationEntry> Entries,
    string? Incarnation = null);

/// <summary>
/// Whether <see cref="IConversationStore.AppendTurnAsync"/> stored the turn or found it already stored.
/// </summary>
public enum ConversationAppendStatus
{
    /// <summary>The turn was stored by this call.</summary>
    Appended = 0,

    /// <summary>A turn with the same id was already stored, so nothing changed.</summary>
    AlreadyAppended = 1,
}

/// <summary>
/// The outcome of <see cref="IConversationStore.AppendTurnAsync"/>.
/// </summary>
/// <param name="Status">Whether this call stored the turn.</param>
/// <param name="FirstSequence">The sequence of the turn's first entry.</param>
/// <param name="NextSequence">The conversation's next sequence right after the turn was committed.</param>
/// <param name="BaseMoved">
/// <see langword="true"/> when other turns were committed after <see cref="ConversationTurn.BaseSequence"/>,
/// so the turn was produced without seeing them.
/// </param>
public sealed record ConversationAppendResult(
    ConversationAppendStatus Status,
    long FirstSequence,
    long NextSequence,
    bool BaseMoved);
