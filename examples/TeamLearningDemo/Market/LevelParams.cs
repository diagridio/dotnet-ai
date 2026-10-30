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

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TeamLearningDemo.Market;

/// <summary>
/// The skill's tunable parameters for one side (support or resistance). A skill version is just a
/// <c>params.json</c> of per-side overrides of these defaults, so "learning" a new version means
/// proposing a different set — never new code.
/// </summary>
public sealed record LevelParams
{
    // structure
    public int SwingK { get; init; } = 4;               // a swing is the extreme of the 2k+1 candles around it
    public double ClusterTolPct { get; init; } = 0.25;  // swings within this distance merge into one level
    public double TouchTolPct { get; init; } = 0.15;    // a candle "tests" a level if its low or high is this close
    public double VpBinPct { get; init; } = 0.10;       // volume-profile bin size
    // zone placement
    public double ZoneWidthPct { get; init; } = 0.50;   // capped by Levels.MaxWidthPct
    public double ZoneAnchor { get; init; } = 0.5;      // share of the zone on the price side of the level
    public double MinDistancePct { get; init; }         // skip levels closer than this to the close ...
    public double MinDistanceAtr { get; init; }         // ... or closer than this many ATR(14)s
    // scoring
    public double WTouches { get; init; } = 1.0;        // how often price reacted at the level
    public double WVolume { get; init; } = 1.0;         // volume-profile weight at the level's price
    public double WReaction { get; init; } = 0.5;       // relative volume of the swing candles
    public double WRecency { get; init; } = 0.5;
    public double WConfluence { get; init; } = 1.0;     // other methods agreeing on the same price
    public double WFlip { get; init; } = 1.0;           // broken resistance acting as support, or the reverse
    public double WDistance { get; init; }              // >0 prefers deeper levels, <0 nearer ones
    // abstention and carry-over
    public double MinScore { get; init; }               // below this the side abstains from a fresh zone
    public double TrendGatePct { get; init; } = 99.0;   // abstain from supports after a 7d fall larger than this
    public int CarryDays { get; init; } = 1;            // on abstention, keep yesterday's zone if still valid

    /// <summary>
    /// Every parameter a proposal may change, with its JSON name and allowed range. A proposal outside
    /// this catalogue or these ranges is rejected before anything runs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Min, double Max, bool Integer)> Catalogue =
        new Dictionary<string, (double, double, bool)>
        {
            ["swing_k"] = (2, 10, true),
            ["cluster_tol_pct"] = (0.05, 1.0, false),
            ["touch_tol_pct"] = (0.05, 0.5, false),
            ["vp_bin_pct"] = (0.05, 0.5, false),
            ["zone_width_pct"] = (0.1, 0.5, false),
            ["zone_anchor"] = (0, 1, false),
            ["min_distance_pct"] = (0, 3, false),
            ["min_distance_atr"] = (0, 5, false),
            ["w_touches"] = (-3, 5, false),
            ["w_volume"] = (-3, 5, false),
            ["w_reaction"] = (-3, 5, false),
            ["w_recency"] = (-3, 5, false),
            ["w_confluence"] = (-3, 5, false),
            ["w_flip"] = (-3, 5, false),
            ["w_distance"] = (-3, 3, false),
            ["min_score"] = (0, 6, false),
            ["trend_gate_pct"] = (1, 99, false),
            ["carry_days"] = (0, 3, true),
        };

    /// <summary>Applies JSON overrides; throws <see cref="ArgumentException"/> naming the first invalid one.</summary>
    public LevelParams With(JsonObject? overrides)
    {
        var p = this;
        foreach (var (key, node) in overrides ?? [])
        {
            if (!Catalogue.TryGetValue(key, out var range))
            {
                throw new ArgumentException($"unknown parameter '{key}'; allowed: {string.Join(", ", Catalogue.Keys)}");
            }
            if (node is not JsonValue v || !v.TryGetValue<double>(out var x) || !double.IsFinite(x))
            {
                throw new ArgumentException($"'{key}' must be a number");
            }
            if (x < range.Min || x > range.Max || (range.Integer && x != Math.Floor(x)))
            {
                throw new ArgumentException(
                    $"'{key}' = {x.ToString(CultureInfo.InvariantCulture)} is outside [{range.Min}, {range.Max}]"
                    + (range.Integer ? " or not an integer" : ""));
            }
            p = key switch
            {
                "swing_k" => p with { SwingK = (int)x },
                "cluster_tol_pct" => p with { ClusterTolPct = x },
                "touch_tol_pct" => p with { TouchTolPct = x },
                "vp_bin_pct" => p with { VpBinPct = x },
                "zone_width_pct" => p with { ZoneWidthPct = x },
                "zone_anchor" => p with { ZoneAnchor = x },
                "min_distance_pct" => p with { MinDistancePct = x },
                "min_distance_atr" => p with { MinDistanceAtr = x },
                "w_touches" => p with { WTouches = x },
                "w_volume" => p with { WVolume = x },
                "w_reaction" => p with { WReaction = x },
                "w_recency" => p with { WRecency = x },
                "w_confluence" => p with { WConfluence = x },
                "w_flip" => p with { WFlip = x },
                "w_distance" => p with { WDistance = x },
                "min_score" => p with { MinScore = x },
                "trend_gate_pct" => p with { TrendGatePct = x },
                "carry_days" => p with { CarryDays = (int)x },
                // The catalogue check above admits only these keys: a key that gets here is in the catalogue
                // but missing from this switch, a bug to fail on rather than an override to drop silently.
                _ => throw new UnreachableException($"parameter '{key}' is in the catalogue but not applied"),
            };
        }
        return p;
    }
}

/// <summary>A published skill version: <c>params.json</c> holds a version number and per-side overrides.</summary>
public sealed record SkillParams(int Version, LevelParams Support, LevelParams Resistance, JsonObject Document)
{
    /// <summary>The parameters for <paramref name="side"/>.</summary>
    public LevelParams For(Side side) => side switch
    {
        Side.Support => Support,
        Side.Resistance => Resistance,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };

    /// <summary>The published version: the one owner of reading <c>skill/params.json</c>.</summary>
    public static async Task<SkillParams> LoadAsync(string paramsFile, CancellationToken cancellationToken = default) =>
        Parse(await File.ReadAllTextAsync(paramsFile, cancellationToken));

    public static SkillParams Parse(string json)
    {
        var doc = JsonNode.Parse(json)!.AsObject();
        return new SkillParams(
            doc["version"]?.GetValue<int>() ?? 1,
            new LevelParams().With(doc["support"] as JsonObject),
            new LevelParams().With(doc["resistance"] as JsonObject),
            doc);
    }

    /// <summary>A candidate version: the same document with one side's overrides changed.</summary>
    public SkillParams WithChanges(Side side, JsonObject changes)
    {
        var doc = (JsonObject)Document.DeepClone();
        var sideDoc = doc[side.Name()] as JsonObject ?? new JsonObject();
        foreach (var (key, value) in changes)
        {
            sideDoc[key] = value?.DeepClone();
        }
        doc[side.Name()] = sideDoc;
        return Parse(doc.ToJsonString());
    }

    public string ToJson() => Document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
