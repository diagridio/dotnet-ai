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

namespace TeamLearningDemo;

public sealed record LearnRequest(
    [property: JsonPropertyName("cutoff")] DateTime? Cutoff = null);

/// <param name="Proposal">The engineer's proposal as JSON: side, parameter changes, rationale.</param>
/// <param name="Stage">"invalid", "rejected_by_backtest" or "promoted".</param>
/// <param name="Published">True when the backtest promoted the proposal and it became the next version.</param>
public sealed record LearnResult(
    [property: JsonPropertyName("taskReport")] string TaskReport,
    [property: JsonPropertyName("proposal")] string? Proposal,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("verdict")] string VerdictJson,
    [property: JsonPropertyName("published")] bool Published,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("lesson")] string Lesson);

// ---------------------------------------------------------------- activity input/output

/// <param name="DataEnd">The run's backtest data window ends at the last UTC midnight at or before this.</param>
public sealed record BaselineInput(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("dataEnd")] DateTimeOffset DataEnd);

public sealed record Baseline(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("verdictJson")] string VerdictJson,
    [property: JsonPropertyName("reportJson")] string ReportJson);

/// <param name="ChangesJson">A JSON object of parameter overrides, e.g. {"trend_gate_pct": 3}.</param>
/// <param name="DataEnd">The same backtest data window the baseline was scored on.</param>
public sealed record ProposalInput(
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("changesJson")] string ChangesJson,
    [property: JsonPropertyName("dataEnd")] DateTimeOffset DataEnd);

/// <param name="Valid">False when the proposal used unknown parameters or values out of range.</param>
/// <param name="ResultJson">The validation error, or the backtest verdict against the published version.</param>
public sealed record ProposalVerdict(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("promote")] bool Promote,
    [property: JsonPropertyName("resultJson")] string ResultJson);

public sealed record PublishInput(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("baseVersion")] int BaseVersion,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("changesJson")] string ChangesJson,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("lesson")] string Lesson);
