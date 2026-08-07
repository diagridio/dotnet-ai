// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

// Demonstrates Skills support (https://github.com/diagridio/dotnet-ai/issues/34): an agent with
// skills sourced all three ways MAF supports — file-based (SKILL.md), inline (AgentInlineSkill),
// and class-based (AgentClassSkill<T>) — mixed via AgentSkillsProviderBuilder, plus a script that
// requires human approval before it runs.

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

// A real IToolApprovalHandler should await an actual human decision (e.g. poll a data store that a
// Slack workflow or admin UI updates out of band — safe to do here since this runs inside a Dapr
// Workflow *activity*, not the orchestrator, so it may take as long as it needs). This demo instead
// approves automatically after logging, purely so the endpoint below produces a response without
// requiring a human in the loop.
builder.Services.AddSingleton<IToolApprovalHandler, ConsoleApprovingToolApprovalHandler>();

var unitConverterSkillPath = Path.Combine(AppContext.BaseDirectory, "skills", "unit-converter");

builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SkillsAgent",
        conversationComponentName: "conversation-ollama",
        instructions: "You are a helpful assistant.",
        serviceLifetime: ServiceLifetime.Singleton)
    .WithSkills("SkillsAgent", skills => skills
        // File-based: discovered from skills/unit-converter/SKILL.md (+ references/notes.md).
        // A script runner is required by MAF whenever any file-based source is configured, even
        // when (like here) the skill itself defines no scripts.
        .UseFileSkill(unitConverterSkillPath, scriptRunner: (_, _, _, _, _) =>
            throw new NotSupportedException("The unit-converter skill has no scripts."))
        // Inline: defined directly in code, no files involved.
        .UseSkill(new AgentInlineSkill(
            name: "joke-teller",
            description: "Tells a short, work-appropriate joke on request.",
            instructions: "When asked for a joke, tell exactly one short, clean joke. Do not explain it."))
        // Class-based: instructions + an [AgentSkillScript]-attributed method live together on one
        // C# class. UseScriptApproval() means run_skill_script for THIS script (and any other
        // skill's scripts) is gated by IToolApprovalHandler before it actually runs.
        .UseSkill(new GreetingSkill())
        .UseScriptApproval());

var app = builder.Build();

app.MapPost("/ask", async (IDaprAgentInvoker invoker, AskRequest req, CancellationToken ct) =>
{
    var agent = invoker.GetAgent("SkillsAgent");
    var result = await invoker.RunAgentAsync(agent, req.Prompt, cancellationToken: ct);
    return Results.Ok(new { response = result.Text });
});

app.Run();

sealed record AskRequest(string Prompt);

/// <summary>
/// Class-based skill: rewrites a casual greeting as a formal one via an attributed script.
/// </summary>
sealed class GreetingSkill : AgentClassSkill<GreetingSkill>
{
    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        name: "greeting-formalizer",
        description: "Rewrites a casual greeting as a formal, business-appropriate one.");

    protected override string Instructions =>
        "When asked to formalize a greeting, run the formalize-greeting script with the casual text.";

    [AgentSkillScript("formalize-greeting")]
    public static string Formalize(string casualGreeting) =>
        $"Good day. {casualGreeting.Trim().TrimEnd('!', '.')}. I hope this message finds you well.";
}

/// <summary>
/// Demo-only <see cref="IToolApprovalHandler"/> — see the registration comment above for what a
/// real implementation should do instead.
/// </summary>
sealed class ConsoleApprovingToolApprovalHandler : IToolApprovalHandler
{
    public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($">>> Approving script call '{request.ToolName}' for agent '{request.AgentName}' (demo auto-approval)");
        return Task.FromResult(ToolApprovalDecision.Approve("Auto-approved by SkillsDemo."));
    }
}
