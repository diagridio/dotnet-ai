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

using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// When and how a stored conversation is compacted: its older turns are summarized once the history an agent
/// loads grows past <see cref="TriggerTokens"/>, and later runs load the summary instead of those turns.
/// </summary>
public sealed class ConversationCompactionOptions
{
    /// <summary>
    /// The default <see cref="SummarizationPrompt"/>.
    /// </summary>
    public const string DefaultSummarizationPrompt =
        "You compact the history of a conversation between a user and an AI assistant so the assistant can continue it. " +
        "Write a concise summary that keeps every fact, decision, constraint, open question and tool result the assistant " +
        "may still need, and what the user asked for. Leave out pleasantries and repetition. Reply with the summary only.";

    /// <summary>
    /// Compaction runs after a run whose loaded history is estimated at more than this many tokens. Required.
    /// </summary>
    /// <remarks>
    /// Tokens are estimated as characters / 4 over the stored history; the context window estimates characters / 4 over
    /// the whole prompt, so leave a margin between the two.
    /// </remarks>
    public int TriggerTokens { get; set; }

    /// <summary>
    /// At most how many of the most recent turns are kept word for word. Fewer are kept when they add up to more than
    /// half of <see cref="TriggerTokens"/>, so one compaction leaves room for several more turns; the latest turn
    /// is always kept. At least 1.
    /// </summary>
    public int KeepRecentTurns { get; set; } = 4;

    /// <summary>
    /// The most the summary call is sent, in estimated tokens: the earlier summary plus the oldest turns that fit.
    /// Turns that do not fit are summarized by later compactions. <see langword="null"/> uses
    /// <see cref="TriggerTokens"/>. Keep it within the summarizing model's context window.
    /// </summary>
    public int? MaxSummaryInputTokens { get; set; }

    /// <summary>
    /// The instructions for the summary. The earlier summary, if any, and the turns to compact are sent after them.
    /// </summary>
    public string SummarizationPrompt { get; set; } = DefaultSummarizationPrompt;

    /// <summary>
    /// The chat client that writes summaries, or <see langword="null"/> to use the agent's own.
    /// </summary>
    public IChatClient? ChatClient { get; set; }

    /// <summary>
    /// Whether history estimated at <paramref name="tokens"/> is past <see cref="TriggerTokens"/>. The one rule the LLM
    /// call (which flags it) and the compaction (which re-checks it) both use.
    /// </summary>
    internal bool IsDue(long tokens) => tokens > TriggerTokens;

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentException">A setting is out of range.</exception>
    internal void Validate()
    {
        if (TriggerTokens <= 0)
        {
            throw new ArgumentException($"{nameof(TriggerTokens)} must be positive.", nameof(TriggerTokens));
        }

        if (KeepRecentTurns < 1)
        {
            throw new ArgumentException($"{nameof(KeepRecentTurns)} must be at least 1.", nameof(KeepRecentTurns));
        }

        if (MaxSummaryInputTokens is <= 0)
        {
            throw new ArgumentException($"{nameof(MaxSummaryInputTokens)} must be positive.", nameof(MaxSummaryInputTokens));
        }

        if (string.IsNullOrWhiteSpace(SummarizationPrompt))
        {
            throw new ArgumentException($"{nameof(SummarizationPrompt)} cannot be empty.", nameof(SummarizationPrompt));
        }
    }
}
