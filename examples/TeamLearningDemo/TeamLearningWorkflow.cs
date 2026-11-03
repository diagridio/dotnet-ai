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
using System.Text.Json.Nodes;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace TeamLearningDemo;

/// <summary>
/// One learning run: the analyst does the task with the published skill, the engineer proposes new
/// parameters aimed at where the skill fails, the backtest judges them, and they become the next skill
/// version only if they are promoted. The curator records the lesson either way.
/// </summary>
/// <remarks>
/// Agents only propose; validation, the verdict and the publication are activities. The workflow body
/// branches only on recorded activity and agent results, so it replays to the same decisions.
/// </remarks>
public sealed class TeamLearningWorkflow : Workflow<LearnRequest, LearnResult>
{
    private const int EngineerAttempts = 3;

    public override async Task<LearnResult> RunAsync(WorkflowContext context, LearnRequest input)
    {
        var runId = context.InstanceId;
        var cutoff = (input.Cutoff?.ToUniversalTime() ?? context.CurrentUtcDateTime.Date)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        // The backtest's data window, fixed once per run (replay-safe): every activity scores on the same split,
        // even when the run crosses a daily close between the baseline and a later proposal.
        var dataEnd = new DateTimeOffset(context.CurrentUtcDateTime.Date, TimeSpan.Zero);

        var baseline = await context.CallActivityAsync<Baseline>(nameof(PrepareBaselineActivity), new BaselineInput(runId, dataEnd));

        // 1. The task, done with the published skill.
        var analyst = await context.RunAgentAsync(context.GetAgent("AnalystAgent"),
            $"Using the btc-support-resistance skill, report the BTCUSDT support and resistance zones for the "
            + $"cutoff {cutoff}: run its levels script with that cutoff and return the report JSON the skill defines.");
        var taskReport = analyst.Text ?? string.Empty;

        // 2. The engineer studies the backtest and proposes new parameters; invalid ones go back to it.
        var engineer = context.GetAgent("EngineerAgent");
        var history = new List<WorkflowChatMessage>();
        var message = $"The published skill (v{baseline.Version}), per side, on the test split:\n{baseline.VerdictJson}\n\n"
                      + $"Its precision and coverage by regime on the train split:\n{baseline.ReportJson}\n\n"
                      + "Find the weakness that matters most (a large group of windows with low precision) and propose "
                      + "parameter changes for one side aimed at it.";
        JsonObject? proposal = null;
        string? side = null;
        JsonObject? changes = null;
        var verdict = new ProposalVerdict(false, false, "{}");
        for (var attempt = 1; attempt <= EngineerAttempts; attempt++)
        {
            var turn = await context.RunAgentWithHistoryAsync(engineer, message, history);
            history.AddRange(turn.TurnMessages);
            proposal = ParseObject(turn.Response.Text);
            // A model can answer "side": 1 or "side": {...}; that is a malformed answer to retry, not a crash.
            side = proposal?["side"] is JsonValue sideValue && sideValue.TryGetValue<string>(out var text) ? text : null;
            changes = proposal?["changes"] as JsonObject;
            if (side is null || changes is null)
            {
                message = """Answer with only the JSON object: {"side": "...", "changes": {...}, "rationale": "..."}.""";
                continue;
            }
            verdict = await context.CallActivityAsync<ProposalVerdict>(nameof(EvaluateProposalActivity),
                new ProposalInput(side, changes.ToJsonString(), dataEnd));
            if (verdict.Valid)
            {
                break;
            }
            message = $"The proposal was rejected before testing:\n{verdict.ResultJson}\nFix it and answer with the JSON again.";
        }
        var stage = !verdict.Valid ? "invalid" : verdict.Promote ? "promoted" : "rejected_by_backtest";

        // 3. The curator records what was learned; it does not change the verdict.
        var curator = await context.RunAgentAsync(context.GetAgent("SkillCuratorAgent"),
            $"Proposal: {proposal?.ToJsonString() ?? "(none)"}\nIt ended as '{stage}'. Backtest result:\n{verdict.ResultJson}\n\n"
            + "In two or three sentences, write the lesson for the team: why it likely worked or failed, and what "
            + "to try next. Use only the numbers given.");
        var lesson = curator.Text ?? string.Empty;

        // 4. Publish only what the backtest promoted.
        var version = baseline.Version;
        if (verdict.Promote && proposal is not null && side is not null && changes is not null)
        {
            version = await context.CallActivityAsync<int>(nameof(PublishActivity), new PublishInput(runId, baseline.Version,
                side, changes.ToJsonString(), proposal["rationale"]?.ToString() ?? "", lesson));
        }
        return new LearnResult(taskReport, proposal?.ToJsonString(), stage, verdict.ResultJson, verdict.Promote, version, lesson);
    }

    /// <summary>The outermost JSON object in a model's answer (models wrap JSON in prose or fences).</summary>
    private static JsonObject? ParseObject(string? text)
    {
        var start = text?.IndexOf('{') ?? -1;
        var end = text?.LastIndexOf('}') ?? -1;
        if (start < 0 || end <= start)
        {
            return null;
        }
        try
        {
            // JsonObject reads its properties lazily: a duplicate key throws on first access, so read them here.
            var parsed = JsonNode.Parse(text![start..(end + 1)]) as JsonObject;
            _ = parsed?.Count;
            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }
}
