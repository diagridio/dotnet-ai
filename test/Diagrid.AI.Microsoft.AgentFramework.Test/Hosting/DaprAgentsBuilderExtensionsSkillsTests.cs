// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

// AgentSkill/AgentInlineSkill/AgentSkillsProvider/AgentSkillsProviderBuilder are marked
// [Experimental("MAAI001")] upstream by MAF. WithSkills(...) itself carries the same marker
// (propagated to our own callers) — this file consumes both, so the warning is suppressed here
// exactly like DaprAgentsBuilderExtensions.WithSkills' own implementation.
#pragma warning disable MAAI001

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderExtensionsSkillsTests
{
    // =========================================================================
    // WithSkills(builder, agentName, params AgentSkill[])
    // =========================================================================

    [Fact]
    public void WithSkills_Array_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithSkills("agent", MakeSkill("s")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSkills_Array_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithSkills(agentName, MakeSkill("s")));
    }

    [Fact]
    public void WithSkills_Array_NullSkills_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() => builder.WithSkills("agent", (AgentSkill[])null!));
    }

    [Fact]
    public void WithSkills_Array_UnsupportedBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithSkills(new FakeBuilder(), "agent", MakeSkill("s")));
    }

    [Fact]
    public void WithSkills_Array_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithSkills("agent", MakeSkill("s"));

        Assert.Same(builder, result);
    }

    [Fact]
    public async Task WithSkills_Array_AttachesAgentSkillsProvider_ContributingSkillCatalogAndTools()
    {
        var services = new ServiceCollection();
        var chatClient = new TestChatClient();
        services.AddSingleton<IChatClient>(chatClient);
        var builder = services.AddDaprAgents();

        builder.WithAgent("skilled-agent", "Be helpful.");
        builder.WithSkills("skilled-agent", MakeSkill("weather", "Look up the weather."));

        var serviceProvider = services.BuildServiceProvider();
        var registration = FindRegistration(services, "skilled-agent");
        registration.Factory(serviceProvider);

        var config = serviceProvider.GetRequiredService<ChatClientRegistry>().Get("skilled-agent");
        Assert.NotNull(config);
        var provider = Assert.Single(config!.ContextProviders!);
        Assert.IsType<AgentSkillsProvider>(provider);

        // End-to-end through the same activity AgentRunWorkflow uses, proving the skill's catalog
        // and load_skill/read_skill_resource/run_skill_script tools actually surface.
        var toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
        var agentRegistry = serviceProvider.GetRequiredService<AgentRegistry>();
        var activity = new ResolveAgentContextActivity(
            serviceProvider.GetRequiredService<ChatClientRegistry>(),
            toolRegistry,
            agentRegistry,
            serviceProvider,
            NullLogger<ResolveAgentContextActivity>.Instance);

        var output = await activity.RunAsync(
            new TestWorkflowActivityContext("instance-1"),
            new ResolveAgentContextInput("skilled-agent", null));

        Assert.Contains("weather", output.Instructions);
        Assert.Contains("load_skill", output.ToolNames!);
        Assert.Contains("read_skill_resource", output.ToolNames!);
    }

    [Fact]
    public async Task WithSkills_Configure_FileBasedSkill_DiscoveredFromSkillMdFile()
    {
        // Covers the third discovery mechanism (file-based, via SKILL.md) end-to-end, alongside
        // the inline (WithSkills_Array_...) and class-based (WithSkills_Configure_... below via
        // AgentClassSkill<T> is exercised in ResolveAgentContextActivityTests/sandbox verification)
        // mechanisms — all three funnel through the exact same AgentSkill base type.
        //
        // The skill directory's leaf name must match a valid skill name (confirmed empirically —
        // MAF's file-based discovery silently excludes directories whose name fails
        // AgentSkillFrontmatter's name validation, e.g. a long GUID-suffixed name, with no error
        // surfaced) so the unique-per-test-run part goes on the PARENT directory instead.
        var skillDir = Path.Combine(Path.GetTempPath(), "dnai-skills-test-" + Guid.NewGuid().ToString("N"), "pdf-tools");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
            """
            ---
            name: pdf-tools
            description: Tools and guidance for extracting text from PDF documents.
            ---

            When asked to work with a PDF, extract text with pdftotext.
            """);

        try
        {
            var services = new ServiceCollection();
            var chatClient = new TestChatClient();
            services.AddSingleton<IChatClient>(chatClient);
            var builder = services.AddDaprAgents();

            builder.WithAgent("file-skill-agent", "Be helpful.");
            builder.WithSkills("file-skill-agent", b => b
                .UseFileSkill(skillDir)
                .UseFileScriptRunner((_, _, _, _, _) => throw new NotSupportedException("no scripts in this fixture")));

            var serviceProvider = services.BuildServiceProvider();
            FindRegistration(services, "file-skill-agent").Factory(serviceProvider);

            var activity = new ResolveAgentContextActivity(
                serviceProvider.GetRequiredService<ChatClientRegistry>(),
                serviceProvider.GetRequiredService<ToolRegistry>(),
                serviceProvider.GetRequiredService<AgentRegistry>(),
                serviceProvider,
                NullLogger<ResolveAgentContextActivity>.Instance);

            var output = await activity.RunAsync(
                new TestWorkflowActivityContext("instance-1"),
                new ResolveAgentContextInput("file-skill-agent", null));

            Assert.Contains("pdf-tools", output.Instructions);

            var loadSkill = serviceProvider.GetRequiredService<ToolRegistry>().Get("file-skill-agent", "load_skill");
            Assert.NotNull(loadSkill);
            var loaded = await loadSkill!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["skillName"] = "pdf-tools" }));
            Assert.Contains("pdftotext", loaded?.ToString());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(skillDir)!, recursive: true);
        }
    }

    // =========================================================================
    // WithSkills(builder, agentName, Action<AgentSkillsProviderBuilder>)
    // =========================================================================

    [Fact]
    public void WithSkills_Configure_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithSkills("agent", b => b.UseSkill(MakeSkill("s"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSkills_Configure_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithSkills(agentName, b => b.UseSkill(MakeSkill("s"))));
    }

    [Fact]
    public void WithSkills_Configure_NullCallback_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() => builder.WithSkills("agent", (Action<AgentSkillsProviderBuilder>)null!));
    }

    [Fact]
    public void WithSkills_Configure_UnsupportedBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithSkills(new FakeBuilder(), "agent", b => b.UseSkill(MakeSkill("s"))));
    }

    [Fact]
    public void WithSkills_Configure_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithSkills("agent", b => b.UseSkill(MakeSkill("s")));

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithSkills_Configure_InvokesCallbackWithBuilder_AndAttachesResult()
    {
        var services = new ServiceCollection();
        var chatClient = new TestChatClient();
        services.AddSingleton<IChatClient>(chatClient);
        var builder = services.AddDaprAgents();
        var configured = false;

        builder.WithAgent("configured-agent", "Be helpful.");
        builder.WithSkills("configured-agent", b =>
        {
            configured = true;
            b.UseSkill(MakeSkill("s")).UseScriptApproval();
        });

        var serviceProvider = services.BuildServiceProvider();
        var registration = FindRegistration(services, "configured-agent");
        registration.Factory(serviceProvider);

        Assert.True(configured);
        var config = serviceProvider.GetRequiredService<ChatClientRegistry>().Get("configured-agent");
        Assert.IsType<AgentSkillsProvider>(Assert.Single(config!.ContextProviders!));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static AgentInlineSkill MakeSkill(string name, string? description = null) =>
        new(name: name, description: description ?? $"Skill '{name}'.", instructions: $"Follow the '{name}' skill's guidance.");

    private static AgentFactoryRegistration FindRegistration(IServiceCollection services, string agentName) =>
        services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Single(registration => registration?.Name == agentName)!;

    private sealed class FakeBuilder : IAgentsBuilder
    {
        public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory) => this;

        public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory) => this;

        public IAgentsBuilder WithCatalyst(DiagridCatalystOptions options) => this;
        public IAgentsBuilder WithCatalyst() => this;
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
