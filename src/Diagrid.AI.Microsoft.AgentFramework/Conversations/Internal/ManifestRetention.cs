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

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

/// <summary>
/// The manifest's bounded lists: the most recent turns, kept for idempotent appends, and the most recent
/// summaries, kept for loads. Both bound a persisted document.
/// </summary>
internal static class ManifestRetention
{
    /// <summary>
    /// Records a summary, keeping the most recent ones. A load pinned to a prefix whose summary was dropped fails
    /// (see <see cref="ConversationManifest.SummariesDroppedMax"/>); every other load still finds its summary.
    /// </summary>
    public static void AddSummary(ConversationManifest manifest, SummaryRecord added)
    {
        var all = new List<SummaryRecord>((manifest.Summaries?.Count ?? 0) + 1);
        if (manifest.Summaries is not null)
        {
            all.AddRange(manifest.Summaries);
        }

        all.Add(added);
        if (all.Count > ConversationManifest.MaxSummaries)
        {
            var dropped = all.Count - ConversationManifest.MaxSummaries;
            manifest.SummariesDroppedMax = all.Take(dropped).Max(r => r.Sequence);
            all = all.GetRange(dropped, ConversationManifest.MaxSummaries);
        }

        manifest.Summaries = all;
    }

    /// <summary>
    /// The turns with <paramref name="added"/> appended, keeping only the most recent <paramref name="window"/>.
    /// </summary>
    public static List<TurnRecord> TrimTurns(List<TurnRecord>? turns, TurnRecord added, int window)
    {
        var all = new List<TurnRecord>((turns?.Count ?? 0) + 1);
        if (turns is not null)
        {
            all.AddRange(turns);
        }

        all.Add(added);
        return all.Count > window ? all.GetRange(all.Count - window, window) : all;
    }

    /// <summary>
    /// Finds the summary a load of the first <paramref name="upTo"/> entries uses: the latest one inside the prefix,
    /// or null when there is none. Returns false when that summary was dropped, so the prefix cannot be loaded as
    /// it was first read.
    /// </summary>
    public static bool TryFindSummary(ConversationManifest manifest, long upTo, out SummaryRecord? summary)
    {
        // The latest summary inside the prefix stands for everything below what it covers. A prefix pinned
        // before a summary was appended never includes it, so a run's history does not change mid-run.
        summary = manifest.Summaries?.Where(r => r.Sequence < upTo).MaxBy(r => r.Sequence);
        return summary is not null || !(manifest.SummariesDroppedMax < upTo);
    }
}
