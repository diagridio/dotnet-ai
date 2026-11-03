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

using System.Text;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Plans a compaction for <see cref="CompactConversationActivity"/>: which turns to summarize, and the transcript the
/// summary call is given. Pure: the activity loads and stores the conversation and makes the call.
/// </summary>
internal static class CompactionPlanner
{
    /// <summary>
    /// Picks what to summarize: the oldest turns that fit <see cref="ConversationCompactionOptions.MaxSummaryInputTokens"/>
    /// together with the earlier summary (at least one turn), leaving the most recent turns: at most
    /// <see cref="ConversationCompactionOptions.KeepRecentTurns"/>, within half of the trigger, and always the latest.
    /// Returns <see langword="null"/> when nothing can be summarized.
    /// </summary>
    internal static (List<ConversationEntry> Older, long CoversUpTo)? PlanCompaction(
        List<List<ConversationEntry>> turns, ConversationEntry? previous, ConversationCompactionOptions options)
    {
        static long Tokens(IEnumerable<ConversationEntry> entries) => TokenEstimate.Of(entries.Select(e => e.Message));

        var keepBudget = options.TriggerTokens / 2;
        var kept = 1;
        long keptTokens = Tokens(turns.Count > 0 ? turns[^1] : []);
        while (kept < options.KeepRecentTurns && kept < turns.Count)
        {
            var candidate = Tokens(turns[turns.Count - 1 - kept]);
            if (keptTokens + candidate > keepBudget)
            {
                break;
            }

            keptTokens += candidate;
            kept++;
        }

        var summarizable = turns.Count - kept;
        if (summarizable <= 0)
        {
            return null;
        }

        var inputBudget = options.MaxSummaryInputTokens ?? options.TriggerTokens;
        var used = previous is null ? 0 : TokenEstimate.Of([previous.Message]);
        var take = 0;
        while (take < summarizable)
        {
            var size = Tokens(turns[take]);
            if (take > 0 && used + size > inputBudget)
            {
                break;
            }

            used += size;
            take++;
        }

        var older = turns.Take(take).SelectMany(t => t).ToList();
        return (older, turns[take][0].Sequence);
    }

    /// <summary>
    /// Groups entries into turns: consecutive entries committed by the same turn.
    /// </summary>
    internal static List<List<ConversationEntry>> SplitTurnsByTurnId(IEnumerable<ConversationEntry> entries)
    {
        var turns = new List<List<ConversationEntry>>();
        foreach (var entry in entries)
        {
            if (turns.Count == 0 || turns[^1][0].TurnId != entry.TurnId)
            {
                turns.Add([]);
            }

            turns[^1].Add(entry);
        }

        return turns;
    }

    /// <summary>
    /// The text the summary call is given: the earlier summary, if any, then the turns to summarize, cut to
    /// <see cref="ConversationCompactionOptions.MaxSummaryInputTokens"/> (else the trigger).
    /// </summary>
    internal static string BuildTranscript(
        ConversationEntry? previous,
        List<ConversationEntry> older,
        ConversationCompactionOptions options)
    {
        // The turns go as one transcript, not as chat messages: replaying tool calls without their tool
        // definitions is rejected by some providers, and reasoning blocks cannot be sent to another model.
        var transcript = new StringBuilder();
        if (previous is not null)
        {
            transcript.AppendLine("Earlier summary:").AppendLine(previous.Message.Content).AppendLine();
        }

        transcript.AppendLine("Conversation to summarize:");
        foreach (var entry in older)
        {
            AppendTranscript(transcript, entry.Message);
        }

        // A single turn larger than the budget is cut, so the summary call always fits.
        var maxChars = (long)(options.MaxSummaryInputTokens ?? options.TriggerTokens) * TokenEstimate.CharsPerToken;
        if (transcript.Length > maxChars)
        {
            transcript.Length = (int)maxChars;
            transcript.AppendLine().Append("[truncated]");
        }

        return transcript.ToString();
    }

    internal static void AppendTranscript(StringBuilder transcript, WorkflowChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.Content))
        {
            transcript.Append(message.Role).Append(": ").AppendLine(message.Content);
        }

        foreach (var call in message.FunctionCalls ?? [])
        {
            transcript.Append(message.Role).Append(" called ").Append(call.Name).Append(' ').AppendLine(call.ArgumentsJson);
        }

        foreach (var result in message.FunctionResults ?? [])
        {
            transcript.Append("tool result: ").AppendLine(result.ResultJson ?? "(none)");
        }
    }
}
