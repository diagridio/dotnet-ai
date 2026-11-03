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

using System.Text.Json;
using System.Text.Json.Nodes;
using TeamLearningDemo.Market;

namespace TeamLearningDemo;

/// <summary>
/// Judges an engineer's proposal: validates it against the parameter catalogue and, if valid, backtests the
/// candidate version against the published one on the test split. The workflow's activity and the
/// <c>--backtest --propose</c> command both use it, so a proposal tried by hand is judged exactly as the team's.
/// </summary>
public static class ProposalJudge
{
    /// <param name="published">The skill version in force.</param>
    /// <param name="sideName">The side the proposal changes: <c>support</c> or <c>resistance</c>.</param>
    /// <param name="changesJson">The proposed parameter overrides, as a JSON object.</param>
    /// <param name="loadBacktest">Loads the backtest; called only for a valid proposal.</param>
    public static async Task<ProposalVerdict> JudgeAsync(
        SkillParams published, string sideName, string changesJson, Func<Task<Backtest>> loadBacktest)
    {
        Side side;
        SkillParams candidate;
        try
        {
            var changes = JsonNode.Parse(changesJson) as JsonObject
                          ?? throw new ArgumentException("changes must be a JSON object");
            if (changes.Count == 0)
            {
                throw new ArgumentException("changes is empty: propose at least one parameter");
            }
            side = SideNames.Parse(sideName);
            candidate = published.WithChanges(side, changes);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException or InvalidOperationException)
        {
            return new ProposalVerdict(false, false, new JsonObject { ["error"] = ex.Message }.ToJsonString());
        }

        var backtest = await loadBacktest();
        var verdict = backtest.Verdict(side, candidate.For(side), published.For(side));
        var result = new JsonObject
        {
            ["side"] = side.Name(),
            ["test"] = JsonSerializer.SerializeToNode(verdict),
            ["train"] = JsonSerializer.SerializeToNode(Backtest.Summarize(backtest.Evaluate(backtest.Train, candidate.For(side), side))),
            ["trainPublished"] = JsonSerializer.SerializeToNode(Backtest.Summarize(backtest.Evaluate(backtest.Train, published.For(side), side))),
        };
        return new ProposalVerdict(true, verdict.Ready, result.ToJsonString());
    }
}
