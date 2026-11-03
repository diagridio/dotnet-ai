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

using System.Text.Json.Serialization;

namespace TeamLearningDemo.Market;

public sealed record Summary(
    [property: JsonPropertyName("windows")] int Windows,
    [property: JsonPropertyName("touched")] int Touched,
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("abstained")] int Abstained,
    [property: JsonPropertyName("coverage")] double Coverage,
    [property: JsonPropertyName("precision")] double Precision);

public sealed record SideVerdict(
    [property: JsonPropertyName("test")] Summary Test,
    [property: JsonPropertyName("baseRate")] double BaseRate,
    [property: JsonPropertyName("pValueVsRandom")] double PValueVsRandom,
    [property: JsonPropertyName("baselinePrecision")] double? BaselinePrecision,
    [property: JsonPropertyName("improvementLowerBound")] double? ImprovementLowerBound,
    [property: JsonPropertyName("checks")] Dictionary<string, bool> Checks,
    [property: JsonPropertyName("ready")] bool Ready);

/// <summary>How one side's recommendation at one cutoff fared over the next 24 hours.</summary>
/// <param name="Zone">The recommended zone, or null when the side abstained.</param>
/// <param name="Context">The window's context, for grouping outcomes by regime.</param>
public sealed record WindowOutcome(Zone? Zone, bool Touched, bool Hit, WindowContext Context);

/// <summary>
/// Replays every daily cutoff of 2026 and scores the zones against the next 24 hours.
/// </summary>
/// <remarks>
/// A support zone is <b>touched</b> when a low reaches zoneHigh within 48 candles, and a <b>hit</b> when
/// it is touched and no 30m candle closes below zoneLow (mirrored for resistance).
/// precision = hits / touched; coverage = touched / windows, so an abstention counts as not touched.
/// The first 70% of cutoffs (train) may be used to choose anything; the last 30% (test) only judges.
/// A side is ready when, on test, coverage ≥ 70%, precision beats a randomly placed valid zone
/// (one-sided binomial test, p &lt; alpha) and, against a baseline, the paired bootstrap's lower bound
/// of the precision gain is above zero.
/// </remarks>
public sealed class Backtest
{
    public const double MinCoverage = 0.70;
    public const double Alpha = 0.05;
    private const double TrainFraction = 0.7;
    private const int Resamples = 2000;
    private static readonly long DataStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly IReadOnlyList<Candle> _candles;
    private readonly Dictionary<(int, LevelParams), WindowContext> _contexts = [];

    private Backtest(IReadOnlyList<Candle> candles)
    {
        _candles = candles;
        var cutoffs = Enumerable.Range(0, candles.Count)
            .Where(i => candles[i].T % CandleStore.DayMs == 0 && i >= Levels.Bars && i + Levels.DayBars <= candles.Count)
            .ToList();
        var split = (int)(cutoffs.Count * TrainFraction);
        Train = cutoffs[..split];
        Test = cutoffs[split..];
    }

    public IReadOnlyList<int> Train { get; }
    public IReadOnlyList<int> Test { get; }
    public string Range => $"train {Train.Count} ({CandleStore.Iso(_candles[Train[0]].T)}..), test {Test.Count} (..{CandleStore.Iso(_candles[Test[^1]].T)})";

    /// <summary>All 2026 candles up to the last UTC midnight at or before <paramref name="asOf"/>.</summary>
    /// <param name="asOf">
    /// Fixes the data window. The CLI passes now; a learning run passes one value for all its activities, so a
    /// run that crosses a daily close still judges the baseline and every candidate on the same split.
    /// </param>
    public static async Task<Backtest> LoadAsync(CandleStore store, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        // An activity input scheduled before the run pinned its data window has no end: fail here, clearly.
        if (asOf == default)
        {
            throw new ArgumentException("the backtest needs the run's data end; this input predates it", nameof(asOf));
        }
        var now = asOf.ToUnixTimeMilliseconds();
        return new Backtest(await store.LoadAsync(DataStart, now - now % CandleStore.DayMs, cancellationToken));
    }

    public List<WindowOutcome> Evaluate(IReadOnlyList<int> cutoffs, LevelParams p, Side side)
    {
        WindowContext ContextAt(int i)
        {
            if (!_contexts.TryGetValue((i, p), out var ctx))
            {
                ctx = Levels.Context(Levels.Window(_candles, i), p);
                _contexts[(i, p)] = ctx;
            }
            return ctx;
        }

        return cutoffs.Select(i =>
        {
            var (zone, _) = Levels.Recommend(_candles, i, side, p, ContextAt);
            var (touched, hit) = zone is null ? (false, false) : Outcome(zone.ZoneLow, zone.ZoneHigh, i, side);
            return new WindowOutcome(zone, touched, hit, ContextAt(i));
        }).ToList();
    }

    public static Summary Summarize(IReadOnlyCollection<WindowOutcome> rows)
    {
        var n = rows.Count;
        var touched = rows.Count(r => r.Touched);
        var hits = rows.Count(r => r.Hit);
        return new Summary(n, touched, hits, rows.Count(r => r.Zone is null),
            n == 0 ? 0 : (double)touched / n, touched == 0 ? 0 : (double)hits / touched);
    }

    /// <summary>Readiness on test for one side, optionally against a baseline (the published version).</summary>
    public SideVerdict Verdict(Side side, LevelParams candidate, LevelParams? baseline, double alpha = Alpha)
    {
        var rows = Evaluate(Test, candidate, side);
        var s = Summarize(rows);
        var baseRate = BaseRate(Test, side);
        var p = s.Touched > 0 ? BinomialTail(s.Hits, s.Touched, baseRate) : 1.0;
        var checks = new Dictionary<string, bool> { ["coverage"] = s.Coverage >= MinCoverage, ["beatsRandom"] = p < alpha };
        double? basePrecision = null, lowerBound = null;
        if (baseline is not null)
        {
            var baseRows = Evaluate(Test, baseline, side);
            basePrecision = Summarize(baseRows).Precision;
            lowerBound = BootstrapLowerBound(rows, baseRows, alpha);
            checks["beatsBaseline"] = lowerBound > 0;
        }
        return new SideVerdict(s, baseRate, p, basePrecision, lowerBound, checks, checks.Values.All(v => v));
    }

    /// <summary>Precision and coverage on train by trend, volatility, method and source: where the skill fails.</summary>
    public Dictionary<string, Dictionary<string, Summary>> RegimeReport(LevelParams p, Side side)
    {
        var rows = Evaluate(Train, p, side);
        var dims = new Dictionary<string, Func<WindowOutcome, string>>
        {
            ["trend7d"] = r => r.Context.Return7dPct < -3 ? "down<-3%" : r.Context.Return7dPct < 3 ? "flat" : "up>3%",
            ["atrPct"] = r => r.Context.AtrPct < 0.25 ? "low<0.25" : r.Context.AtrPct < 0.45 ? "mid" : "high>0.45",
            ["method"] = r => r.Zone?.Method ?? "none (abstained)",
            ["source"] = r => r.Zone?.Source ?? "none (abstained)",
        };
        return dims.ToDictionary(d => d.Key, d => rows.GroupBy(d.Value).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Summarize(g.ToList())));
    }

    private (bool Touched, bool Hit) Outcome(double zl, double zh, int i, Side side)
    {
        bool touched = false, broken = false;
        for (var k = i; k < i + Levels.DayBars; k++)
        {
            var c = _candles[k];
            touched |= side == Side.Support ? c.L <= zh : c.H >= zl;
            broken |= side == Side.Support ? c.C < zl : c.C > zh;
        }
        return (touched, touched && !broken);
    }

    /// <summary>Precision of a zone placed uniformly at random among all valid zones (0.05% grid).</summary>
    private double BaseRate(IReadOnlyList<int> cutoffs, Side side)
    {
        int hits = 0, touched = 0;
        foreach (var i in cutoffs)
        {
            var last = _candles[i - 1].C;
            for (var step = 0; step <= 50; step++)
            {
                double zl, zh;
                if (side == Side.Support)
                {
                    zh = last * (1 - step * 0.0005);
                    zl = zh * (1 - Levels.MaxWidthPct / 100);
                }
                else
                {
                    zl = last * (1 + step * 0.0005);
                    zh = zl / (1 - Levels.MaxWidthPct / 100);
                }
                if (!Levels.WithinConstraints(zl, zh, side, last))
                {
                    continue;
                }
                var (t, h) = Outcome(zl, zh, i, side);
                touched += t ? 1 : 0;
                hits += h ? 1 : 0;
            }
        }
        return touched == 0 ? 0 : (double)hits / touched;
    }

    /// <summary>P(X ≥ k) for X ~ Binomial(n, p).</summary>
    private static double BinomialTail(int k, int n, double p)
    {
        double sum = 0;
        for (var j = k; j <= n; j++)
        {
            sum += Math.Exp(LogChoose(n, j) + j * Math.Log(p) + (n - j) * Math.Log(1 - p));
        }
        return Math.Min(1, sum);
    }

    private static double LogChoose(int n, int k)
    {
        double r = 0;
        for (var i = 1; i <= k; i++)
        {
            r += Math.Log(n - k + i) - Math.Log(i);
        }
        return r;
    }

    /// <summary>One-sided (1 - alpha) lower bound of precision(candidate) - precision(baseline), paired by window.</summary>
    private static double BootstrapLowerBound(List<WindowOutcome> candidate, List<WindowOutcome> baseline, double alpha)
    {
        var rng = new Random(7);  // fixed seed: the same inputs always give the same verdict
        var n = candidate.Count;
        var diffs = new double[Resamples];
        for (var r = 0; r < Resamples; r++)
        {
            int ct = 0, ch = 0, bt = 0, bh = 0;
            for (var j = 0; j < n; j++)
            {
                var i = rng.Next(n);
                ct += candidate[i].Touched ? 1 : 0;
                ch += candidate[i].Hit ? 1 : 0;
                bt += baseline[i].Touched ? 1 : 0;
                bh += baseline[i].Hit ? 1 : 0;
            }
            diffs[r] = (ct == 0 ? 0 : (double)ch / ct) - (bt == 0 ? 0 : (double)bh / bt);
        }
        Array.Sort(diffs);
        return diffs[(int)(alpha * Resamples)];
    }
}
