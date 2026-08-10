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

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

// An inline skill defined directly in code.
var pirateSkill = new AgentInlineSkill(
    name: "pirate-speak",
    description: "Rewrite answers in the voice of a pirate.",
    instructions: "When this skill is loaded, phrase every answer like a boisterous pirate. Arr!",
    license: null,
    compatibility: null,
    allowedTools: null,
    metadata: null,
    serializerOptions: null,
    argumentMarshaler: null);

builder.Services.AddDaprAgents()
    .WithSkills(
        agentName: "SkilledAssistant",
        conversationComponentName: "conversation-ollama",
        instructions: """
                      You are a helpful assistant. When a task matches one of your available skills,
                      call load_skill to retrieve its instructions before answering, and follow them.
                      """,
        configureSkills: skills => skills
            // File-based skills: every SKILL.md found under the given directory.
            .UseFileSkills([Path.Combine(AppContext.BaseDirectory, "skills")])
            // Inline skill defined above.
            .UseSkill(pirateSkill)
            // Class-based skill (see GitCommitSkill below).
            .UseSkill(new GitCommitSkill()));

var app = builder.Build();

app.MapPost("/run", async (IDaprAgentInvoker invoker, RunRequest req, CancellationToken ct) =>
{
    var agent = invoker.GetAgent("SkilledAssistant");
    var result = await invoker.RunAgentAsync(agent, req.Prompt, cancellationToken: ct);
    return Results.Ok(new { response = result.Text });
});

app.Run();

sealed record RunRequest(string Prompt);

/// <summary>
/// A class-based skill: encapsulate the skill's instructions (and optionally resources/scripts) in
/// a type deriving from <see cref="AgentClassSkill{TSelf}"/>.
/// </summary>
public sealed class GitCommitSkill : AgentClassSkill<GitCommitSkill>
{
    public GitCommitSkill()
        : base(argumentMarshaler: null)
    {
    }

    public override AgentSkillFrontmatter Frontmatter { get; } = new AgentSkillFrontmatter(
        name: "git-commit-message",
        description: "Write clear, conventional git commit messages.",
        compatibility: null);

    protected override string Instructions =>
        """
        Write commit messages using the Conventional Commits style: a `type(scope): summary` subject
        line under 72 characters, followed by a blank line and a short body explaining the why.
        """;
}
