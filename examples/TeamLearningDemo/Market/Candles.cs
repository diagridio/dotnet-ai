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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamLearningDemo.Market;

/// <summary>One closed 30-minute BTCUSDT candle: open time (epoch ms), OHLC and base-asset volume.</summary>
public sealed record Candle(
    [property: JsonPropertyName("t")] long T,
    [property: JsonPropertyName("o")] double O,
    [property: JsonPropertyName("h")] double H,
    [property: JsonPropertyName("l")] double L,
    [property: JsonPropertyName("c")] double C,
    [property: JsonPropertyName("v")] double V);

/// <summary>
/// Closed BTCUSDT 30m candles from Binance's public API, cached on disk per exact [start, end) range.
/// </summary>
/// <remarks>
/// Only closed candles of a fixed range are ever used, so the same range always yields the same
/// candles: that is what makes the skill and the backtest deterministic.
/// </remarks>
public sealed class CandleStore(HttpClient http, string cacheDirectory)
{
    public const string Symbol = "BTCUSDT";
    public const long IntervalMs = 30 * 60 * 1000;
    public const long DayMs = 24 * 60 * 60 * 1000;
    private const int Page = 1000;  // Binance's maximum candles per request

    public async Task<IReadOnlyList<Candle>> LoadAsync(long startMs, long endMs, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lastClosed = now - now % IntervalMs;
        if (endMs > lastClosed)
        {
            throw new ArgumentException(
                $"end {Iso(endMs)} is in the future or inside the open candle; use a cutoff at or before {Iso(lastClosed)}");
        }

        Directory.CreateDirectory(cacheDirectory);
        var cache = Path.Combine(cacheDirectory, $"{Symbol}_30m_{startMs}_{endMs}.json");
        if (File.Exists(cache))
        {
            await using var read = File.OpenRead(cache);
            return await JsonSerializer.DeserializeAsync<List<Candle>>(read, cancellationToken: cancellationToken) ?? [];
        }

        var candles = new List<Candle>();
        for (var cursor = startMs; cursor < endMs;)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={Symbol}&interval=30m"
                      + $"&startTime={cursor}&endTime={endMs - 1}&limit={Page}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken));
            var rows = doc.RootElement.EnumerateArray().ToList();
            if (rows.Count == 0)
            {
                break;
            }
            foreach (var r in rows)
            {
                var t = r[0].GetInt64();
                if (t >= startMs && t < endMs)
                {
                    candles.Add(new Candle(t, Num(r[1]), Num(r[2]), Num(r[3]), Num(r[4]), Num(r[5])));
                }
            }
            cursor = rows[^1][0].GetInt64() + IntervalMs;
            if (rows.Count < Page)
            {
                break;
            }
        }

        // Write-then-rename, so a concurrent reader never sees a partial file.
        var tmp = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(candles), cancellationToken);
        File.Move(tmp, cache, overwrite: true);
        return candles;
    }

    /// <summary>ISO-8601 UTC, or epoch ms → epoch ms floored to the 30m grid.</summary>
    public static long ParseCutoff(string value)
    {
        long ms = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            ? epoch
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        return ms - ms % IntervalMs;
    }

    public static string Iso(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static double Num(JsonElement e) => double.Parse(e.GetString()!, CultureInfo.InvariantCulture);
}
