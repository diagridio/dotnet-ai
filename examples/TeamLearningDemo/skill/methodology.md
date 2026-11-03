# Methodology

## Levels

Every level comes from the 336 closed 30m candles before the cutoff (`Market/Levels.cs`):

| Method | What it is |
|---|---|
| `swing_low` / `swing_high` | the extreme of the 2k+1 candles around it; swings within `cluster_tol_pct` merge into one level |
| `flip_resistance` | a swing-high level now below the close: a broken resistance that may act as support |
| `flip_support` | a swing-low level now above the close: a broken support that may act as resistance |
| `volume_node` | a local peak of the volume profile above 1.5x the mean |
| `pivot_S1` ... `pivot_R2` | classic pivots from the last 24h |

Each level is scored per side with the weights in `params.json`. The score combines:

- touches;
- volume-profile weight;
- reaction volume of the swings;
- recency;
- confluence with other methods;
- a flip bonus that grows with the breakout candle's relative volume;
- distance.

The best level becomes a zone at most 0.5% wide, within 3% of the close.

A side abstains when the 7-day trend runs against it (`trend_gate_pct`) or when its best score is
below `min_score`. When it abstains, it keeps the previous day's zone if no close has broken that
zone since (`carry_days`).

## Evaluation

`Market/Backtest.cs` replays every daily cutoff of 2026. The first 70% of cutoffs form the train
split and the last 30% the test split.

| | Touched | Hit |
|---|---|---|
| support | a low reaches `zoneHigh` within 24h | touched and no 30m close below `zoneLow` |
| resistance | a high reaches `zoneLow` within 24h | touched and no 30m close above `zoneHigh` |

- precision = hits / touched;
- coverage = touched / windows, so an abstention counts as not touched.

A proposal for one side is promoted when, on test, three conditions hold:

1. Coverage is at least 70%.
2. Precision beats a zone placed at random among all valid zones (one-sided binomial test,
   p < 0.05).
3. Precision beats the published version (paired bootstrap, 95% lower bound > 0).

## Changelog

### v1: baseline

Per-side parameters chosen by a random search on the train split.

| Side (test split) | Precision | Coverage | Random zones |
|---|---|---|---|
| support | about 55% | about 85% | 48.5% |
| resistance | about 40% | about 72% | 43.4% |

Neither side is reliably better than chance yet.
