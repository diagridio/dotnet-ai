// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies that <see cref="ContextProviderPipeline"/> drives a Microsoft Agent Framework
/// <see cref="AIContextProvider"/> (here a real <c>AgentSkillsProvider</c>) and collects the
/// instructions and invokable tools it contributes, registering the tools so they can later be
/// executed as durable activities.
/// </summary>
public sealed class ContextProviderPipelineTests
{
    private const string AgentName = "skills-agent";

    private static AgentSkillsProvider BuildSkillsProvider() =>
        new AgentSkillsProviderBuilder()
            .UseSkill(new AgentInlineSkill(
                name: "pirate-speak",
                description: "Talk like a pirate",
                instructions: "Always answer like a pirate.",
                license: null,
                compatibility: null,
                allowedTools: null,
                metadata: null,
                serializerOptions: null,
                argumentMarshaler: null))
            .UseOptions(o =>
            {
                o.DisableLoadSkillApproval = true;
                o.DisableReadSkillResourceApproval = true;
            })
            .Build();

    private static AIAgent BuildAgent() =>
        new FakeChatClient().AsAIAgent(instructions: "You are helpful.", name: AgentName);

    [Fact]
    public async Task InvokeAsync_AdvertisesSkillCatalogInInstructions()
    {
        var contribution = await ContextProviderPipeline.InvokeAsync(
            AgentName, BuildAgent(), [BuildSkillsProvider()], new ToolRegistry(), CancellationToken.None);

        var instructions = Assert.Single(contribution.Instructions);
        Assert.Contains("pirate-speak", instructions);
    }

    [Fact]
    public async Task InvokeAsync_ExposesReadOnlySkillTools()
    {
        var contribution = await ContextProviderPipeline.InvokeAsync(
            AgentName, BuildAgent(), [BuildSkillsProvider()], new ToolRegistry(), CancellationToken.None);

        Assert.Contains(contribution.Tools, t => t.Name == "load_skill");
        Assert.Contains(contribution.Tools, t => t.Name == "read_skill_resource");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotExposeApprovalGatedScriptTool()
    {
        // run_skill_script remains approval-gated; the durable runtime cannot perform the approval
        // round-trip yet, so the pipeline must not expose it.
        var contribution = await ContextProviderPipeline.InvokeAsync(
            AgentName, BuildAgent(), [BuildSkillsProvider()], new ToolRegistry(), CancellationToken.None);

        Assert.DoesNotContain(contribution.Tools, t => t.Name == "run_skill_script");
    }

    [Fact]
    public async Task InvokeAsync_RegistersInvokableToolsInToolRegistry()
    {
        var toolRegistry = new ToolRegistry();

        await ContextProviderPipeline.InvokeAsync(
            AgentName, BuildAgent(), [BuildSkillsProvider()], toolRegistry, CancellationToken.None);

        Assert.NotNull(toolRegistry.Get(AgentName, "load_skill"));
        Assert.NotNull(toolRegistry.Get(AgentName, "read_skill_resource"));
        Assert.Null(toolRegistry.Get(AgentName, "run_skill_script"));
    }

    [Fact]
    public async Task InvokeAsync_LoadSkillTool_ReturnsSkillContent()
    {
        // Progressive disclosure in this durable model relies on load_skill returning the skill's
        // content as its result (which is appended to the durable conversation log).
        var toolRegistry = new ToolRegistry();
        await ContextProviderPipeline.InvokeAsync(
            AgentName, BuildAgent(), [BuildSkillsProvider()], toolRegistry, CancellationToken.None);

        var loadSkill = toolRegistry.Get(AgentName, "load_skill")!;
        var result = await loadSkill.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["skillName"] = "pirate-speak" }));

        Assert.Contains("Always answer like a pirate.", result?.ToString());
    }

    [Fact]
    public void TryGetInvokableFunction_PlainFunction_ReturnsTrue()
    {
        var fn = AIFunctionFactory.Create(() => "ok", name: "probe");

        Assert.True(ContextProviderPipeline.TryGetInvokableFunction(fn, out var resolved));
        Assert.Same(fn, resolved);
    }

    private sealed class FakeChatClient : IChatClient
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
