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
using Dapr.Workflow;
using TeamLearningDemo.Market;

namespace TeamLearningDemo;

// The deterministic half of the team. Agents propose; these activities measure and decide, with the
// same Backtest the skill is judged by, so no number in a run is a model's own arithmetic.

/// <summary>Where the demo keeps its files, resolved from the project directory.</summary>
public sealed class TeamPaths(string contentRoot)
{
    public string ParamsFile { get; } = Path.Combine(contentRoot, "skill", "params.json");
    public string MethodologyFile { get; } = Path.Combine(contentRoot, "skill", "methodology.md");
    public string CandleCache { get; } = Path.Combine(contentRoot, "workspace", "cache");
}

/// <summary>Scores the published skill: readiness per side on test, failures by regime on train.</summary>
public sealed class PrepareBaselineActivity(CandleStore store, TeamPaths paths) : WorkflowActivity<BaselineInput, Baseline>
{
    public override async Task<Baseline> RunAsync(WorkflowActivityContext context, BaselineInput input)
    {
        var published = await SkillParams.LoadAsync(paths.ParamsFile);
        var backtest = await Backtest.LoadAsync(store, input.DataEnd);
        var verdict = new JsonObject();
        var report = new JsonObject();
        foreach (var side in Levels.Sides)
        {
            verdict[side.Name()] = JsonSerializer.SerializeToNode(backtest.Verdict(side, published.For(side), baseline: null));
            report[side.Name()] = JsonSerializer.SerializeToNode(backtest.RegimeReport(published.For(side), side));
        }
        return new Baseline(published.Version, verdict.ToJsonString(), report.ToJsonString());
    }
}

/// <summary>
/// Validates the engineer's proposal against the parameter catalogue and, if valid, judges it: the
/// candidate version against the published one on the test split. Only this result can promote it.
/// </summary>
public sealed class EvaluateProposalActivity(CandleStore store, TeamPaths paths) : WorkflowActivity<ProposalInput, ProposalVerdict>
{
    public override async Task<ProposalVerdict> RunAsync(WorkflowActivityContext context, ProposalInput input)
    {
        var published = await SkillParams.LoadAsync(paths.ParamsFile);
        return await ProposalJudge.JudgeAsync(published, input.Side, input.ChangesJson, () => Backtest.LoadAsync(store, input.DataEnd));
    }
}

/// <summary>
/// Publishes a promoted proposal as the next skill version: the new overrides go into
/// <c>skill/params.json</c> and the change into the methodology's changelog.
/// </summary>
public sealed class PublishActivity(TeamPaths paths) : WorkflowActivity<PublishInput, int>
{
    public override async Task<int> RunAsync(WorkflowActivityContext context, PublishInput input)
    {
        var published = await SkillParams.LoadAsync(paths.ParamsFile);
        if (published.Version != input.BaseVersion)
        {
            return published.Version;  // published already (a retry) or by another run: do not apply it twice
        }
        var next = published.WithChanges(SideNames.Parse(input.Side), JsonNode.Parse(input.ChangesJson)!.AsObject());
        var version = input.BaseVersion + 1;
        next.Document["version"] = version;
        next.Document["notes"] = $"v{version}: {input.Side} changes {input.ChangesJson}, promoted by learning run {input.RunId}.";

        await File.AppendAllTextAsync(paths.MethodologyFile,
            $"\n### v{version}: {input.Side} {input.ChangesJson}\n\n- **Learning run:** `{input.RunId}`\n"
            + $"- **Why:** {input.Rationale}\n- **Lesson:** {input.Lesson}\n");
        // params.json last: until it changes, the previous version stays in force.
        await File.WriteAllTextAsync(paths.ParamsFile, next.ToJson() + "\n");
        return version;
    }
}
