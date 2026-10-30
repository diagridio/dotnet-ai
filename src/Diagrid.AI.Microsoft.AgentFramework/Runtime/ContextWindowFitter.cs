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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// The messages of one LLM call and where each part starts: the head (the system message and the context providers'
/// messages), then the earlier turns, then the run's own messages.
/// </summary>
/// <param name="Messages">The messages in the order they are sent.</param>
/// <param name="HeadCount">How many messages the head holds.</param>
/// <param name="HistoryCount">How many earlier-turn messages follow the head.</param>
/// <param name="HistoryStartsWithSummary">Whether the first earlier-turn message is a stored summary.</param>
internal readonly record struct PromptLayout(
    List<ChatMessage> Messages,
    int HeadCount,
    int HistoryCount,
    bool HistoryStartsWithSummary);

/// <summary>
/// The outcome of <see cref="ContextWindowFitter.Fit"/>.
/// </summary>
/// <param name="Messages">The messages to send, or <see langword="null"/> when they cannot fit.</param>
/// <param name="Dropped">How many earlier-turn messages were left out.</param>
/// <param name="RequiredTokens">The estimated tokens of what is always sent.</param>
internal readonly record struct ContextWindowFit(List<ChatMessage>? Messages, int Dropped, long RequiredTokens);

/// <summary>
/// Fits an LLM call into a context window (<c>WithContextWindow</c>) by leaving out earlier turns, whole and oldest
/// first. Pure: it only changes what one call sends.
/// </summary>
/// <remarks>
/// A turn starts at a user message, so the history sent never opens with an assistant message or a tool result, and a
/// tool call is never separated from its results; a stored summary is a turn of its own. The head, system messages in
/// the history other than a stored summary (a persona the caller passed in, for instance) and the run's own messages
/// are always sent. Tokens are estimated over the messages as they are sent (<see cref="TokenEstimate"/>), so
/// reasoning that is not sent back does not count.
/// </remarks>
internal static class ContextWindowFitter
{
    /// <summary>
    /// The messages to send within <paramref name="maxContextTokens"/> less <paramref name="maxOutputTokens"/>, or
    /// <see langword="null"/> messages when what is always sent alone does not fit.
    /// </summary>
    public static ContextWindowFit Fit(PromptLayout layout, int maxContextTokens, int maxOutputTokens)
    {
        var messages = layout.Messages;
        var historyStart = layout.HeadCount;
        var historyEnd = layout.HeadCount + layout.HistoryCount;
        bool AlwaysSent(int i) =>
            i < historyStart || i >= historyEnd
            || (messages[i].Role == ChatRole.System && !(layout.HistoryStartsWithSummary && i == historyStart));

        var budget = (long)maxContextTokens - maxOutputTokens;
        var requiredTokens = TokenEstimate.Of(Enumerable.Range(0, messages.Count).Where(AlwaysSent).Select(i => messages[i]));
        if (requiredTokens > budget)
        {
            return new ContextWindowFit(null, 0, requiredTokens);
        }

        var turns = SplitTurnsByUserMessage(messages, historyStart, historyEnd);
        var droppable = turns.Select(t => t.Where(i => !AlwaysSent(i)).ToList()).ToList();
        var historyTokens = droppable.Sum(t => TokenEstimate.Of(t.Select(i => messages[i])));
        var drop = new HashSet<int>();
        for (var t = 0; t < turns.Count && requiredTokens + historyTokens > budget; t++)
        {
            historyTokens -= TokenEstimate.Of(droppable[t].Select(i => messages[i]));
            drop.UnionWith(droppable[t]);
        }

        return drop.Count == 0
            ? new ContextWindowFit(messages, 0, requiredTokens)
            : new ContextWindowFit([.. messages.Where((_, i) => !drop.Contains(i))], drop.Count, requiredTokens);
    }

    /// <summary>
    /// Splits <paramref name="messages"/>[start..end) into turns of message indexes, each starting at a user message.
    /// Messages before the first user message (a stored summary) form a turn of their own.
    /// </summary>
    internal static List<List<int>> SplitTurnsByUserMessage(List<ChatMessage> messages, int start, int end)
    {
        var turns = new List<List<int>>();
        for (var i = start; i < end; i++)
        {
            if (turns.Count == 0 || messages[i].Role == ChatRole.User)
            {
                turns.Add([]);
            }

            turns[^1].Add(i);
        }

        return turns;
    }
}
