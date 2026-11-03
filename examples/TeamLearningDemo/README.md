# TeamLearningDemo

A small team of agents improves a runtime skill by using it. It publishes a new version of the
skill **only when a deterministic backtest says the change is better**.

The task is to compute the BTCUSDT support and resistance zones on Binance 30m candles for a cutoff
time. The skill is a class-based MAF skill, [`BtcSupportResistanceSkill`](BtcSupportResistanceSkill.cs):

- its `levels` script runs deterministic C# ([`Market/Levels.cs`](Market/Levels.cs)) over closed
  candles, so the model never does the math;
- the published version lives in [`skill/params.json`](skill/params.json);
- the changelog lives in [`skill/methodology.md`](skill/methodology.md), exposed to the agent as the
  `methodology` resource.

## The pattern

`POST /learn` starts `TeamLearningWorkflow`:

| Step | Who | What |
|---|---|---|
| 1 | `PrepareBaselineActivity` | scores the published skill: readiness per side on test, failures by regime on train |
| 2 | **AnalystAgent** | does the task with the skill and reports the zones |
| 3 | **EngineerAgent** | reads where the skill fails and proposes new parameters for one side |
| 4 | `EvaluateProposalActivity` | validates the proposal against the parameter catalogue and backtests it against the published version; an invalid proposal goes back to the engineer, up to 3 attempts |
| 5 | **SkillCuratorAgent** | writes the lesson |
| 6 | `PublishActivity` | if the backtest promoted the proposal, publishes it as the next skill version |

The split is the point:

- **Agents only propose:** parameters and a lesson.
- **Activities validate, measure and decide.**

No model can talk a change into the skill, and the workflow replays to the same decisions.

The engineer proposes **parameters, not code**. A skill version is a set of values from a fixed
catalogue (`LevelParams.Catalogue`): scoring weights, trend gate, abstention threshold, carry-over,
and so on. Each value has a range, and anything outside the catalogue is rejected before it runs.
Learning stays deterministic and safe, with no generated code to sandbox.

### When a change is "better"

A side (support or resistance) improves when, on a held-out test split of 2026 data
([`Market/Backtest.cs`](Market/Backtest.cs)), three conditions hold:

1. Coverage stays at 70% or more.
2. Precision beats a zone placed at random (binomial test).
3. Precision beats the published version (paired bootstrap).

[`skill/methodology.md`](skill/methodology.md) defines precision and coverage.

## Going further

This example is the minimal shape of the pattern. It grows naturally in several directions:

- scouts that harvest ideas from curated references into a persistent backlog;
- engineers that experiment in parallel on a shared blackboard (train data only);
- a synthesizer that combines their findings;
- multiple-comparison correction when many candidates share one test split;
- pausing runs when the model provider's usage limit is reached.

## Run

You need `dapr init` and network access to the Binance public API, for the candles. The candles
are cached under the git-ignored `workspace/`.

The agents use the `conversation-ollama` component in `examples/Components/`. The analyst calls the
skill's script and the engineer reasons over backtest results. A small local model is unlikely to do
either reliably, so point the component at a capable, tool-calling model first, for example a larger
local Ollama model or a hosted one through `conversation.openai`.

From `examples/`:

```
dapr run --app-id teamlearning --app-port 5061 --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path Components/
```

From `examples/TeamLearningDemo/`, in another shell:

```
dotnet run
curl -s -X POST localhost:5061/learn -H 'content-type: application/json' -d '{"cutoff": "2026-10-19T00:00:00Z"}'
curl -s localhost:5061/learn/<instanceId>
```

Without the agents:

```
dotnet run -- --backtest                                           # score the published skill
dotnet run -- --backtest --propose support '{"trend_gate_pct": 3}' # judge a proposal like the workflow does
```
