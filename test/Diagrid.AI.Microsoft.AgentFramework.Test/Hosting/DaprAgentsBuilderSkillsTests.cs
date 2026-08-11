// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

/// <summary>
/// Verifies the <see cref="DaprAgentsBuilderExtensions.WithSkills"/> registration path: argument
/// validation, that a factory registration is added, and that materializing the factory registers
/// the skills provider and its read-only tools so the durable runtime can advertise and execute them.
/// </summary>
public sealed class DaprAgentsBuilderSkillsTests
{
    private const string AgentName = "skill-agent";
    private const string Component = "conversation-test";

    private static void UsePirateSkill(AgentSkillsProviderBuilder builder) =>
        builder.UseSkill(new AgentInlineSkill(
            name: "pirate-speak",
            description: "Talk like a pirate",
            instructions: "Always answer like a pirate.",
            license: null,
            compatibility: null,
            allowedTools: null,
            metadata: null,
            serializerOptions: null,
            argumentMarshaler: null));

    [Fact]
    public void WithSkills_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithSkills(AgentName, Component, "instructions", UsePirateSkill));
    }

    [Fact]
    public void WithSkills_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithSkills(null!, Component, "instructions", UsePirateSkill));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSkills_WhitespaceComponent_Throws(string component)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() =>
            builder.WithSkills(AgentName, component, "instructions", UsePirateSkill));
    }

    [Fact]
    public void WithSkills_NullInstructions_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithSkills(AgentName, Component, null!, UsePirateSkill));
    }

    [Fact]
    public void WithSkills_NullConfigureSkills_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithSkills(AgentName, Component, "instructions", null!));
    }

    [Fact]
    public void WithSkills_AddsRegistrationWithAgentName()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithSkills(AgentName, Component, "Be helpful.", UsePirateSkill, description: "A pirate.");

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == AgentName && r.ChatClientKey == Component);
    }

    [Fact]
    public void WithSkills_MaterializedFactory_RegistersProviderAndReadOnlyTools()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        builder.WithSkills(AgentName, Component, "Be helpful.", UsePirateSkill);

        // Supply a test chat client under the component key so no Dapr sidecar is needed. This is
        // registered after WithSkills, so it wins keyed resolution.
        var chatClient = new TestChatClient();
        services.AddKeyedSingleton<IChatClient>(Component, chatClient);
        var provider = services.BuildServiceProvider();

        var registration = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Single(r => r?.Name == AgentName)!;

        var agent = registration.Factory(provider);

        Assert.Equal(AgentName, agent.Name);

        var config = provider.GetRequiredService<ChatClientRegistry>().Get(AgentName);
        Assert.NotNull(config);
        Assert.Equal("Be helpful.", config!.Instructions);
        Assert.NotNull(config.ContextProviders);
        Assert.Single(config.ContextProviders!);

        var toolRegistry = provider.GetRequiredService<ToolRegistry>();
        Assert.NotNull(toolRegistry.Get(AgentName, "load_skill"));
        Assert.NotNull(toolRegistry.Get(AgentName, "read_skill_resource"));
        Assert.Null(toolRegistry.Get(AgentName, "run_skill_script"));
    }

    [Fact]
    public void WithSkills_FileBasedSkill_BuildsWithoutCallerSuppliedScriptRunner()
    {
        // File-based skills require a script runner to be present at build time. WithSkills supplies
        // a disabled default so consumers can use SKILL.md skills without wiring script execution.
        var skillsRoot = Directory.CreateTempSubdirectory("skills-test");
        try
        {
            var skillDir = Directory.CreateDirectory(Path.Combine(skillsRoot.FullName, "git-workflow"));
            File.WriteAllText(Path.Combine(skillDir.FullName, "SKILL.md"),
                """
                ---
                name: git-workflow
                description: Guidance for common git workflows.
                ---
                When asked to create a branch, tell the user to run `git switch -c <name>`.
                """);

            var services = new ServiceCollection();
            var builder = services.AddDaprAgents();
            builder.WithSkills(AgentName, Component, "Be helpful.", b => b.UseFileSkills([skillsRoot.FullName]));

            services.AddKeyedSingleton<IChatClient>(Component, new TestChatClient());
            var provider = services.BuildServiceProvider();

            var registration = services
                .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
                .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
                .Single(r => r?.Name == AgentName)!;

            // Must not throw "File-based skill sources require a script runner".
            var agent = registration.Factory(provider);

            Assert.Equal(AgentName, agent.Name);
            var toolRegistry = provider.GetRequiredService<ToolRegistry>();
            Assert.NotNull(toolRegistry.Get(AgentName, "load_skill"));
        }
        finally
        {
            skillsRoot.Delete(recursive: true);
        }
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
