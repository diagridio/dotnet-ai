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

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using TeamLearningDemo.Market;

namespace TeamLearningDemo;

/// <summary>
/// The skill the team learns: BTCUSDT support and resistance zones, as a class-based MAF skill.
/// </summary>
/// <remarks>
/// Every number comes from the <c>levels</c> script (deterministic C# over closed Binance candles),
/// never from the model's own arithmetic. The script reads the published <c>skill/params.json</c> on
/// every call, so a version the team publishes takes effect without a restart.
/// </remarks>
public sealed class BtcSupportResistanceSkill(CandleStore store, TeamPaths paths)
    : AgentClassSkill<BtcSupportResistanceSkill>
{
    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        name: "btc-support-resistance",
        description: "Computes BTCUSDT support and resistance zones on Binance 30-minute candles for a cutoff time. "
                     + "Use it whenever a task asks about Bitcoin or BTC support, resistance, key levels or price "
                     + "zones, or about broken levels turning into support or resistance.");

    // The numbers come from the constants the script uses, so the instructions cannot drift from the code.
    protected override string Instructions => string.Create(CultureInfo.InvariantCulture, $$"""
        Answer with the `levels` script; never compute levels, distances or percentages yourself, and never
        quote prices from memory. Pass the cutoff as an ISO-8601 UTC time such as 2026-10-19T00:00:00Z; it
        must not be in the future. The window is the {{Levels.Bars}} closed {{CandleStore.IntervalMs / 60_000}}-minute candles ({{Levels.Bars / Levels.DayBars}} days) before it.

        For each side, `recommended` is the zone to report (at most {{Levels.MaxWidthPct}}% wide, within {{Levels.MaxDistancePct}}% of the last close),
        or null with an `abstainReason`: an abstention is a valid answer, report it instead of inventing a
        zone. `method` says where the level came from (swing_low/high, flip_resistance = a broken
        resistance now acting as support, flip_support = the reverse, volume_node, pivot_*), and `source`
        is "carried" when today had no reliable level and yesterday's unbroken zone is kept.

        Report exactly this JSON, with values copied from the script output:
        {"cutoff": "...", "lastClose": 0.0,
         "support": {"zoneLow": 0.0, "zoneHigh": 0.0, "method": "", "source": "", "abstainReason": null},
         "resistance": {"zoneLow": 0.0, "zoneHigh": 0.0, "method": "", "source": "", "abstainReason": null},
         "notes": "one or two sentences"}
        The `methodology` resource explains how zones are built and how reliable the published version is.
        """);

    [AgentSkillScript("levels")]
    [Description("Support and resistance zones for BTCUSDT at a cutoff time, computed from closed Binance 30m candles.")]
    public async Task<string> LevelsAsync(
        [Description("The cutoff, ISO-8601 UTC, e.g. 2026-10-19T00:00:00Z.")] string cutoff,
        [Description("How many ranked alternative zones to return per side.")] int top = 3)
    {
        try
        {
            var end = CandleStore.ParseCutoff(cutoff);
            var published = await SkillParams.LoadAsync(paths.ParamsFile);
            var required = Levels.Sides.Max(side => Levels.RequiredCandles(published.For(side)));
            var candles = await store.LoadAsync(end - required * CandleStore.IntervalMs, end);
            if (candles.Count < required)
            {
                return Error($"only {candles.Count} of {required} candles available before {CandleStore.Iso(end)}");
            }

            var n = candles.Count;
            var result = new JsonObject
            {
                ["symbol"] = CandleStore.Symbol,
                ["windowEnd"] = CandleStore.Iso(end),
                ["lastClose"] = candles[^1].C,
                ["skillVersion"] = published.Version,
            };
            foreach (var side in Levels.Sides)
            {
                var p = published.For(side);
                WindowContext ContextAt(int i) => Levels.Context(Levels.Window(candles, i), p);
                var (zone, reason) = Levels.Recommend(candles, n, side, p, ContextAt);
                result[side.Name()] = new JsonObject
                {
                    ["recommended"] = JsonSerializer.SerializeToNode(zone),
                    ["abstainReason"] = reason,
                    ["candidates"] = JsonSerializer.SerializeToNode(Levels.Candidates(ContextAt(n), side, p, Math.Clamp(top, 1, 10))),
                };
            }
            return result.ToJsonString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or HttpRequestException)
        {
            return Error(ex.Message);  // returned to the model as data, so it can fix its call
        }
    }

    [AgentSkillResource("methodology")]
    [Description("How the zones are built, how the skill is evaluated, and every published change.")]
    public string Methodology => File.ReadAllText(paths.MethodologyFile);

    private static string Error(string message) => new JsonObject { ["error"] = message }.ToJsonString();
}
