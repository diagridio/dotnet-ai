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

/// <summary>A support or resistance zone recommended for one side at one cutoff.</summary>
public sealed record Zone(
    [property: JsonPropertyName("side")] Side Side,
    [property: JsonPropertyName("level")] double Level,
    [property: JsonPropertyName("zoneLow")] double ZoneLow,
    [property: JsonPropertyName("zoneHigh")] double ZoneHigh,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("touches")] int Touches,
    [property: JsonPropertyName("confluence")] int Confluence,
    [property: JsonPropertyName("volumeRatio")] double VolumeRatio,
    [property: JsonPropertyName("breakoutVolume")] double BreakoutVolume,
    [property: JsonPropertyName("distancePct")] double DistancePct,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("source")] string Source = "fresh");

/// <summary>Everything about one window that does not depend on the scoring weights.</summary>
public sealed record WindowContext(
    double LastClose, double AtrPct, double Return7dPct, IReadOnlyList<RawLevel> Levels);

public sealed record RawLevel(
    double Level, Side Side, string Method, int Swings, int LastIdx, double ReactionVolume,
    double BreakoutVolume, double Recency, int Touches, double Volume, int Confluence);

/// <summary>
/// Deterministic support/resistance zones over the 336 closed 30m candles (7 days) before a cutoff.
/// </summary>
/// <remarks>
/// Levels come from swing lows/highs (clustered), broken swings that flipped role (a broken resistance
/// now acting as support, and the reverse, scored higher when the breakout candle had heavy volume),
/// volume-profile nodes and daily pivots. Each level is scored, the best one per side becomes a zone at
/// most 0.5% wide within 3% of the close, and a side may abstain — keeping yesterday's zone if no close
/// has broken it since.
/// </remarks>
public static class Levels
{
    public const int Bars = 336;     // analysis window: 7 days of 30m candles
    public const int DayBars = 48;   // 24h: the evaluation horizon and the carry-over step
    public const double MaxWidthPct = 0.5;
    public const double MaxDistancePct = 3.0;
    public static readonly Side[] Sides = [Side.Support, Side.Resistance];

    /// <summary>
    /// Candles a recommendation for <paramref name="p"/> needs before its cutoff: the window, plus one day
    /// per carry-over step, since <see cref="Recommend"/> may look back <see cref="LevelParams.CarryDays"/> days.
    /// </summary>
    public static int RequiredCandles(LevelParams p) => Bars + p.CarryDays * DayBars;

    /// <summary>The analysis window of the cutoff at index <paramref name="i"/>: candles[i-Bars..i).</summary>
    public static IReadOnlyList<Candle> Window(IReadOnlyList<Candle> candles, int i) => candles.Skip(i - Bars).Take(Bars).ToList();

    public static WindowContext Context(IReadOnlyList<Candle> c, LevelParams p)
    {
        var n = c.Count;
        var last = c[n - 1].C;
        var medVol = Median(c.Select(x => x.V));
        var lows = Cluster(c, SwingPoints(c, p.SwingK, low: true), low: true, p.ClusterTolPct, medVol);
        var highs = Cluster(c, SwingPoints(c, p.SwingK, low: false), low: false, p.ClusterTolPct, medVol);
        var (nodes, volumeAt) = VolumeProfile(c, p.VpBinPct, last);

        var raw = new List<(double Level, Side Side, string Method, int Swings, int LastIdx, double Reaction, double Breakout, double Recency)>();
        foreach (var s in lows)
        {
            var side = s.Level < last ? Side.Support : Side.Resistance;
            var flip = side == Side.Resistance;  // a swing low above the close is a broken support
            raw.Add((s.Level, side, flip ? "flip_support" : "swing_low", s.Swings, s.LastIdx, s.Reaction,
                flip ? BreakoutVolume(c, s.Level, s.LastIdx, upward: false, medVol) : 0, (double)s.LastIdx / (n - 1)));
        }
        foreach (var s in highs)
        {
            var side = s.Level > last ? Side.Resistance : Side.Support;
            var flip = side == Side.Support;  // a swing high below the close is a broken resistance
            raw.Add((s.Level, side, flip ? "flip_resistance" : "swing_high", s.Swings, s.LastIdx, s.Reaction,
                flip ? BreakoutVolume(c, s.Level, s.LastIdx, upward: true, medVol) : 0, (double)s.LastIdx / (n - 1)));
        }
        foreach (var level in nodes)
        {
            raw.Add((level, level < last ? Side.Support : Side.Resistance, "volume_node", 0, 0, 0, 0, 0));
        }
        foreach (var (name, level) in Pivots(c))
        {
            raw.Add((level, level < last ? Side.Support : Side.Resistance, "pivot_" + name, 0, 0, 0, 0, 1));
        }

        var levels = raw.Select(r => new RawLevel(r.Level, r.Side, r.Method, r.Swings, r.LastIdx, r.Reaction, r.Breakout,
                r.Recency, Touches(c, r.Level, p.TouchTolPct), volumeAt(r.Level),
                raw.Count(o => o.Method != r.Method && Math.Abs(o.Level - r.Level) / last * 100 <= p.ClusterTolPct)))
            .ToList();
        return new WindowContext(last, Atr(c) / last * 100, (last / c[0].O - 1) * 100, levels);
    }

    /// <summary>Ranked, distinct fresh zones for one side.</summary>
    public static List<Zone> Candidates(WindowContext ctx, Side side, LevelParams p, int top = 5)
    {
        var last = ctx.LastClose;
        var raw = ctx.Levels.Where(r => r.Side == side).ToList();
        var maxTouch = Math.Max(1, raw.Count == 0 ? 1 : raw.Max(r => r.Touches));
        var minDistance = Math.Max(p.MinDistancePct, p.MinDistanceAtr * ctx.AtrPct);
        var zones = new List<Zone>();
        foreach (var r in raw)
        {
            var distance = Math.Abs(last - r.Level) / last * 100;
            if (distance < minDistance)
            {
                continue;
            }
            var (zl, zh) = Place(r.Level, side, last, p);
            if (!WithinConstraints(zl, zh, side, last))
            {
                continue;
            }
            var flip = r.Method.StartsWith("flip_", StringComparison.Ordinal);
            var score = p.WTouches * r.Touches / maxTouch
                        + p.WVolume * Math.Min(r.Volume, 3) / 3
                        + p.WReaction * Math.Min(r.ReactionVolume, 3) / 3
                        + p.WRecency * r.Recency
                        + p.WConfluence * Math.Min(r.Confluence, 3) / 3.0
                        + (flip ? p.WFlip * (0.5 + 0.5 * Math.Min(r.BreakoutVolume, 3) / 3) : 0)
                        + p.WDistance * distance / MaxDistancePct;
            zones.Add(new Zone(side, Math.Round(r.Level, 2), Math.Round(zl, 2), Math.Round(zh, 2), r.Method, r.Touches,
                r.Confluence, Math.Round(r.Volume, 3), Math.Round(r.BreakoutVolume, 3), Math.Round(distance, 3),
                Math.Round(score, 4)));
        }
        // Nearest first on ties, so equal scores always resolve the same way; then drop near-duplicates.
        var distinct = new List<Zone>();
        foreach (var z in zones.OrderByDescending(z => z.Score).ThenBy(z => z.DistancePct))
        {
            if (distinct.All(d => Math.Abs(z.Level - d.Level) / last * 100 > p.ClusterTolPct))
            {
                distinct.Add(z);
            }
        }
        return distinct.Take(top).ToList();
    }

    /// <summary>The zone for the cutoff at index <paramref name="i"/> (window candles[i-Bars..i)), with carry-over.</summary>
    /// <param name="contextAt">context() of the window ending at an index; the backtest passes a cached one.</param>
    public static (Zone? Zone, string? AbstainReason) Recommend(
        IReadOnlyList<Candle> candles, int i, Side side, LevelParams p, Func<int, WindowContext> contextAt, int? depth = null)
    {
        depth ??= p.CarryDays;
        var (zone, reason) = Fresh(contextAt(i), side, p);
        if (zone is not null)
        {
            return (zone, null);
        }
        if (depth <= 0 || i - DayBars < Bars)
        {
            return (null, reason);
        }
        var (previous, _) = Recommend(candles, i - DayBars, side, p, contextAt, depth - 1);
        if (previous is not null && StillValid(previous, candles, i - DayBars, i, side, candles[i - 1].C))
        {
            return (previous with { Source = "carried" }, null);
        }
        return (null, reason);
    }

    public static bool WithinConstraints(double zl, double zh, Side side, double last)
    {
        if ((zh - zl) / zh * 100 > MaxWidthPct + 1e-9)
        {
            return false;
        }
        return side == Side.Support
            ? zl < last && zl >= last * (1 - MaxDistancePct / 100)
            : zh > last && zh <= last * (1 + MaxDistancePct / 100);
    }

    private static (Zone?, string?) Fresh(WindowContext ctx, Side side, LevelParams p)
    {
        if ((side == Side.Support && ctx.Return7dPct < -p.TrendGatePct) || (side == Side.Resistance && ctx.Return7dPct > p.TrendGatePct))
        {
            return (null, "trend_gate");
        }
        var best = Candidates(ctx, side, p, top: 1);
        if (best.Count == 0)
        {
            return (null, "no_valid_candidate");
        }
        return best[0].Score < p.MinScore ? (null, "below_min_score") : (best[0], null);
    }

    /// <summary>A carried zone survives if no close broke it since it was issued and it still fits.</summary>
    private static bool StillValid(Zone zone, IReadOnlyList<Candle> c, int from, int to, Side side, double last)
    {
        for (var k = from; k < to; k++)
        {
            if ((side == Side.Support && c[k].C < zone.ZoneLow) || (side == Side.Resistance && c[k].C > zone.ZoneHigh))
            {
                return false;
            }
        }
        return WithinConstraints(zone.ZoneLow, zone.ZoneHigh, side, last);
    }

    /// <summary>Zone around a level; the width is a share of zoneHigh so it does not drift with price.</summary>
    private static (double Low, double High) Place(double level, Side side, double last, LevelParams p)
    {
        var w = Math.Min(p.ZoneWidthPct, MaxWidthPct) / 100;
        if (side == Side.Support)
        {
            var zh = Math.Min(level * (1 + w * p.ZoneAnchor), last);
            return (zh * (1 - w), zh);
        }
        var zl = Math.Max(level * (1 - w * p.ZoneAnchor), last);
        return (zl, zl / (1 - w));
    }

    private static double Atr(IReadOnlyList<Candle> c, int period = 14)
    {
        var trs = new List<double>();
        for (var i = 1; i < c.Count; i++)
        {
            trs.Add(Math.Max(c[i].H - c[i].L, Math.Max(Math.Abs(c[i].H - c[i - 1].C), Math.Abs(c[i].L - c[i - 1].C))));
        }
        var tail = trs.Skip(Math.Max(0, trs.Count - period)).ToList();
        return tail.Count == 0 ? 0 : tail.Average();
    }

    private static List<int> SwingPoints(IReadOnlyList<Candle> c, int k, bool low)
    {
        var points = new List<int>();
        for (var i = k; i < c.Count - k; i++)
        {
            var isSwing = true;
            for (var j = i - k; j <= i + k && isSwing; j++)
            {
                isSwing = low ? c[i].L <= c[j].L : c[i].H >= c[j].H;
            }
            if (isSwing)
            {
                points.Add(i);
            }
        }
        return points;
    }

    /// <summary>Merges swings within tolPct of the running cluster mean into levels (median price).</summary>
    private static List<(double Level, int Swings, int LastIdx, double Reaction)> Cluster(
        IReadOnlyList<Candle> c, List<int> idx, bool low, double tolPct, double medVol)
    {
        var groups = new List<(List<double> Prices, List<int> Idx, double Mean)>();
        foreach (var (price, i) in idx.Select(i => (Price: low ? c[i].L : c[i].H, I: i)).OrderBy(x => x.Price).ThenBy(x => x.I))
        {
            if (groups.Count > 0 && Math.Abs(price - groups[^1].Mean) / groups[^1].Mean * 100 <= tolPct)
            {
                var g = groups[^1];
                g.Prices.Add(price);
                g.Idx.Add(i);
                groups[^1] = (g.Prices, g.Idx, g.Prices.Average());
            }
            else
            {
                groups.Add(([price], [i], price));
            }
        }
        return groups.Select(g =>
        {
            var sorted = g.Prices.Order().ToList();
            var reaction = medVol > 0 ? g.Idx.Average(i => c[i].V) / medVol : 0;
            return (sorted[sorted.Count / 2], sorted.Count, g.Idx.Max(), reaction);
        }).ToList();
    }

    private static int Touches(IReadOnlyList<Candle> c, double level, double tolPct)
    {
        double lo = level * (1 - tolPct / 100), hi = level * (1 + tolPct / 100);
        return c.Count(x => (lo <= x.L && x.L <= hi) || (lo <= x.H && x.H <= hi));
    }

    /// <summary>Relative volume of the first close through the level after its last swing.</summary>
    private static double BreakoutVolume(IReadOnlyList<Candle> c, double level, int afterIdx, bool upward, double medVol)
    {
        for (var i = afterIdx + 1; i < c.Count; i++)
        {
            if ((upward && c[i].C > level) || (!upward && c[i].C < level))
            {
                return medVol > 0 ? c[i].V / medVol : 0;
            }
        }
        return 0;
    }

    private static (List<double> Nodes, Func<double, double> VolumeAt) VolumeProfile(IReadOnlyList<Candle> c, double binPct, double reference)
    {
        var lo = c.Min(x => x.L);
        var hi = c.Max(x => x.H);
        var size = reference * binPct / 100;
        var n = Math.Max(1, (int)Math.Ceiling((hi - lo) / size));
        var bins = new double[n];
        foreach (var x in c)
        {
            var a = Math.Min(n - 1, (int)((x.L - lo) / size));
            var b = Math.Min(n - 1, (int)((x.H - lo) / size));
            for (var k = a; k <= b; k++)
            {
                bins[k] += x.V / (b - a + 1);
            }
        }
        var mean = bins.Sum() / n;
        var nodes = new List<double>();
        for (var k = 0; k < n; k++)
        {
            var left = k > 0 ? bins[k - 1] : -1;
            var right = k < n - 1 ? bins[k + 1] : -1;
            if (bins[k] >= left && bins[k] >= right && bins[k] > 1.5 * mean)
            {
                nodes.Add(lo + (k + 0.5) * size);
            }
        }
        return (nodes, level =>
        {
            var k = (int)((level - lo) / size);  // truncates toward zero, like the reference implementation
            return k >= 0 && k < n && mean > 0 ? bins[k] / mean : 0;
        });
    }

    /// <summary>Classic pivots from the last 24h: S1, S2, R1, R2.</summary>
    private static IEnumerable<(string Name, double Level)> Pivots(IReadOnlyList<Candle> c)
    {
        var day = c.Skip(c.Count - DayBars).ToList();
        var h = day.Max(x => x.H);
        var l = day.Min(x => x.L);
        var p = (h + l + day[^1].C) / 3;
        yield return ("S1", 2 * p - h);
        yield return ("S2", p - (h - l));
        yield return ("R1", 2 * p - l);
        yield return ("R2", p + (h - l));
    }

    private static double Median(IEnumerable<double> values)
    {
        var s = values.Order().ToList();
        if (s.Count == 0)
        {
            return 0;
        }
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
