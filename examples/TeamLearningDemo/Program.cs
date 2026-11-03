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

// A small team of agents learns a skill by doing a task with it: the analyst uses the skill, the
// engineer proposes better parameters, a deterministic backtest decides, and the proposal is
// published as the next skill version only if it is measurably better.

using System.Globalization;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using TeamLearningDemo;
using TeamLearningDemo.Market;

var builder = WebApplication.CreateBuilder(args);

var paths = new TeamPaths(builder.Environment.ContentRootPath);
var store = new CandleStore(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, paths.CandleCache);

// Without any agent: dotnet run -- --backtest [--propose <side> '<changes JSON>'] (see BacktestCommand).
if (args.Contains("--backtest"))
{
    Environment.ExitCode = await BacktestCommand.RunAsync(args, store, paths);
    return;
}

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(store);

var catalogue = string.Join("\n", LevelParams.Catalogue.Select(c => $"- {c.Key}: {c.Value.Min} to {c.Value.Max}"
                                                                    + (c.Value.Integer ? " (integer)" : "")));

builder.Services.AddDaprAgents(registrations: opt =>
    {
        opt.RegisterWorkflow<TeamLearningWorkflow>();
        opt.RegisterActivity<PrepareBaselineActivity>();
        opt.RegisterActivity<EvaluateProposalActivity>();
        opt.RegisterActivity<PublishActivity>();
    })
    .WithAgent(
        agentName: "AnalystAgent",
        conversationComponentName: "conversation-ollama",
        instructions: """
                      You are the team's analyst. You answer with the btc-support-resistance skill: load it, run
                      its levels script, and report exactly what the script returns, in the report format the
                      skill defines. You never compute or estimate prices yourself.
                      """,
        serviceLifetime: ServiceLifetime.Singleton)
    .WithSkills("AnalystAgent", skills => skills.UseSkill(new BtcSupportResistanceSkill(store, paths)))
    .WithAgent(
        agentName: "EngineerAgent",
        conversationComponentName: "conversation-ollama",
        // The coverage bar and the trend gate's horizon come from the code's constants, not restated here.
        instructions: string.Create(CultureInfo.InvariantCulture, $$"""
                        You are the team's engineer. You read backtest results of the team's skill and propose new
                        parameters for one side (support or resistance) aimed at where it fails. A precision gain
                        only counts if coverage stays at or above {{Backtest.MinCoverage:0%}}.

                        Parameters you may change, with their allowed ranges (anything else is rejected):
                        {{catalogue}}

                        trend_gate_pct makes a side abstain after a {{Levels.Bars / Levels.DayBars}}-day move against it larger than that percent;
                        min_score makes it abstain when its best level is weak; carry_days keeps an unbroken zone
                        from the previous day when it abstains. The w_* weights score levels by touches, volume,
                        reaction volume, recency, confluence, role flips and distance.

                        Answer with only this JSON:
                        {"side": "support" | "resistance", "changes": {"<parameter>": <number>, ...}, "rationale": "..."}
                        """),
        serviceLifetime: ServiceLifetime.Singleton)
    .WithAgent(
        agentName: "SkillCuratorAgent",
        conversationComponentName: "conversation-ollama",
        instructions: """
                      You are the team's skill curator. You turn the outcome of a learning run into a short lesson.
                      The backtest decides what is published; you never change or second-guess its verdict, and you
                      never report a number you were not given.
                      """,
        serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

// POST /learn — start a learning run. Body (optional): {"cutoff": "2026-10-19T00:00:00Z"}
app.MapPost("/learn", async (DaprWorkflowClient client, LearnRequest? request) =>
{
    var id = "learn-" + Guid.NewGuid().ToString("N")[..12];
    await client.ScheduleNewWorkflowAsync(nameof(TeamLearningWorkflow), id, request ?? new LearnRequest());
    return Results.Accepted($"/learn/{id}", new { instanceId = id });
});

// GET /learn/{id} — status, then the outcome: task report, proposal, verdict, published version, lesson.
app.MapGet("/learn/{id}", async (DaprWorkflowClient client, string id, CancellationToken ct) =>
    await client.GetWorkflowStateAsync(id, cancellation: ct) is { } state
        ? Results.Ok(new
        {
            status = state.RuntimeStatus.ToString(),
            result = state.RuntimeStatus == WorkflowRuntimeStatus.Completed ? state.ReadOutputAs<LearnResult>() : null,
            failure = state.FailureDetails?.ErrorMessage,
        })
        : Results.NotFound());

await app.RunAsync();
