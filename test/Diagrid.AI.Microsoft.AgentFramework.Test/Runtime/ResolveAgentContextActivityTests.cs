// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ResolveAgentContextActivityTests
{
    private const string AgentName = "test-agent";

    [Fact]
    public async Task RunAsync_NoContextProviders_ReturnsEmptyOutput()
    {
        var (activity, _, _) = Build();

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Null(output.Instructions);
        Assert.Null(output.Messages);
        Assert.Null(output.ToolNames);
    }

    [Fact]
    public async Task RunAsync_ProviderContributesInstructions_ReturnedVerbatim()
    {
        var provider = new FakeContextProvider { Instructions = "You have access to skills." };
        var (activity, _, _) = Build(provider);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Equal("You have access to skills.", output.Instructions);
    }

    [Fact]
    public async Task RunAsync_MultipleProviders_InstructionsAccumulateInOrder()
    {
        // AIContextProvider.InvokingAsync's own template merges the seed with each provider's
        // contribution (confirmed empirically) — providers are chained, not called independently.
        var providerA = new FakeContextProvider { Instructions = "First." };
        var providerB = new FakeContextProvider { Instructions = "Second." };
        var (activity, _, _) = Build(providerA, providerB);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Equal("First.\nSecond.", output.Instructions);
    }

    [Fact]
    public async Task RunAsync_ProviderContributesTools_RegisteredIntoToolRegistry_AndNamesReturned()
    {
        var tool = AIFunctionFactory.Create(() => "loaded", name: "load_skill");
        var provider = new FakeContextProvider { Tools = [tool] };
        var (activity, _, toolRegistry) = Build(provider);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Equal(["load_skill"], output.ToolNames);
        Assert.Same(tool, toolRegistry.Get(AgentName, "load_skill"));
    }

    [Fact]
    public async Task RunAsync_MultipleProviders_ToolsFromAllProvidersRegistered()
    {
        var toolA = AIFunctionFactory.Create(() => "a", name: "tool_a");
        var toolB = AIFunctionFactory.Create(() => "b", name: "tool_b");
        var providerA = new FakeContextProvider { Tools = [toolA] };
        var providerB = new FakeContextProvider { Tools = [toolB] };
        var (activity, _, toolRegistry) = Build(providerA, providerB);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Equal(["tool_a", "tool_b"], output.ToolNames);
        Assert.Same(toolA, toolRegistry.Get(AgentName, "tool_a"));
        Assert.Same(toolB, toolRegistry.Get(AgentName, "tool_b"));
    }

    [Fact]
    public async Task RunAsync_ProviderContributesMessages_ConvertedToWorkflowChatMessages()
    {
        var provider = new FakeContextProvider { Messages = [new ChatMessage(ChatRole.User, "ephemeral context")] };
        var (activity, _, _) = Build(provider);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.NotNull(output.Messages);
        var msg = Assert.Single(output.Messages!);
        Assert.Equal("user", msg.Role);
        Assert.Equal("ephemeral context", msg.Content);
    }

    [Fact]
    public async Task RunAsync_ProviderContributesNothing_ReturnsEmptyOutput_DoesNotEchoSeed()
    {
        // Regression guard: the seed AIContext must stay empty (no Messages) — otherwise a
        // no-op provider's echo-through would resurface as a spurious "contribution" and get
        // duplicated once CallLlmActivity also sends the real conversation.
        var provider = new FakeContextProvider();
        var (activity, _, _) = Build(provider);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Null(output.Instructions);
        Assert.Null(output.Messages);
        Assert.Null(output.ToolNames);
    }

    [Fact]
    public async Task RunAsync_ProviderThrows_PropagatesException()
    {
        var provider = new FakeContextProvider { ThrowOnInvoking = new InvalidOperationException("boom") };
        var (activity, _, _) = Build(provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null)));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task RunAsync_LazilyResolvesAgent_WhenNotYetMaterialized()
    {
        // Uses AgentRegistry's lazy factory path rather than a pre-materialized agent — this is
        // the FIRST activity called in AgentRunWorkflow, so it must trigger materialization itself
        // exactly like CallLlmActivity/ExecuteToolActivity do.
        var sp = new EmptyServiceProvider();
        var chatClientRegistry = new ChatClientRegistry();
        var toolRegistry = new ToolRegistry();
        var agentRegistry = new AgentRegistry(sp, []);
        var provider = new FakeContextProvider { Instructions = "Lazy." };

        agentRegistry.AddFactory(_ =>
        {
            var agent = new TestAIAgent(AgentName);
            chatClientRegistry.Register(AgentName, new TestChatClient(), null, null, [provider]);
            return agent;
        }, null, AgentName, sp);

        var activity = new ResolveAgentContextActivity(chatClientRegistry, toolRegistry, agentRegistry, sp, NullLogger<ResolveAgentContextActivity>.Instance);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Equal("Lazy.", output.Instructions);
    }

    // =========================================================================
    // AIAgent.CurrentRunContext / AgentRunOptions.AdditionalProperties (issue #66)
    // =========================================================================

    [Fact]
    public async Task RunAsync_EstablishesCurrentRunContext_WithOptionsAndRequestMessages()
    {
        AgentRunContext? captured = null;
        var provider = new FakeContextProvider { OnInvoking = _ => captured = AIAgent.CurrentRunContext };
        var (activity, _, _) = Build(provider);

        var options = new AgentRunOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["sessionId"] = "abc-123" }
        };
        var requestMessages = new List<WorkflowChatMessage> { new() { Role = "user", Content = "hello" } };

        await activity.RunAsync(
            MakeContext(),
            new ResolveAgentContextInput(AgentName, null) { Options = options, RequestMessages = requestMessages });

        var runContext = captured ?? throw new InvalidOperationException("CurrentRunContext was not established.");
        var runOptions = runContext.RunOptions ?? throw new InvalidOperationException("RunOptions was not set.");
        Assert.Same(options, runOptions);
        Assert.Equal("abc-123", runOptions.AdditionalProperties?["sessionId"]);
        Assert.Single(runContext.RequestMessages);
        Assert.Equal("hello", runContext.RequestMessages.Single().Text);
    }

    [Fact]
    public async Task RunAsync_CurrentRunContext_IsClearedAfterActivityCompletes()
    {
        var provider = new FakeContextProvider();
        var (activity, _, _) = Build(provider);

        await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Null(AIAgent.CurrentRunContext);
    }

    [Fact]
    public async Task RunAsync_ProviderThrows_StillClearsCurrentRunContext()
    {
        var provider = new FakeContextProvider { ThrowOnInvoking = new InvalidOperationException("boom") };
        var (activity, _, _) = Build(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null)));

        Assert.Null(AIAgent.CurrentRunContext);
    }

    [Fact]
    public async Task RunAsync_NoContextProviders_SerializedSessionJsonIsNull()
    {
        var (activity, _, _) = Build();

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.Null(output.SerializedSessionJson);
    }

    [Fact]
    public async Task RunAsync_WithProviders_SerializedSessionJsonIsPopulated()
    {
        var provider = new FakeContextProvider();
        var (activity, _, _) = Build(provider);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.NotNull(output.SerializedSessionJson);
    }

    [Fact]
    public async Task RunAsync_ProviderWritesToSessionStateBag_CapturedInSerializedSession()
    {
        // The "one logical AgentSession across resolution and completion" requirement from issue
        // #66: state a provider writes during InvokingAsync must survive into what
        // CompleteAgentContextActivity reconstructs for InvokedAsync.
        //
        // Uses a real ChatClientAgent rather than the TestAIAgent double used elsewhere in this
        // file: TestAIAgent's session serialize/deserialize are fixed no-op stubs (always "{}"/a
        // fresh session), so they can't demonstrate an actual state round-trip.
        var sp = new EmptyServiceProvider();
        var chatClientRegistry = new ChatClientRegistry();
        var toolRegistry = new ToolRegistry();
        var agentRegistry = new AgentRegistry(sp, []);
        var provider = new FakeContextProvider
        {
            OnInvoking = ctx => ctx.Session!.StateBag.SetValue("memory-key", "written-during-invoking")
        };
        var chatClientAgent = new ChatClientAgent(new TestChatClient(), instructions: null, name: AgentName);
        agentRegistry.AddFactory(_ =>
        {
            chatClientRegistry.Register(AgentName, chatClientAgent.ChatClient, null, null, [provider]);
            return chatClientAgent;
        }, null, AgentName, sp);
        var activity = new ResolveAgentContextActivity(chatClientRegistry, toolRegistry, agentRegistry, sp, NullLogger<ResolveAgentContextActivity>.Instance);

        var output = await activity.RunAsync(MakeContext(), new ResolveAgentContextInput(AgentName, null));

        Assert.NotNull(output.SerializedSessionJson);
        var serialized = JsonSerializer.Deserialize<JsonElement>(output.SerializedSessionJson!);
        var session = await chatClientAgent.DeserializeSessionAsync(serialized);
        Assert.True(session.StateBag.TryGetValue<string>("memory-key", out var value));
        Assert.Equal("written-during-invoking", value);
    }

    private static (ResolveAgentContextActivity Activity, ChatClientRegistry ChatClientRegistry, ToolRegistry ToolRegistry) Build(
        params AIContextProvider[] providers)
    {
        var sp = new EmptyServiceProvider();
        var chatClientRegistry = new ChatClientRegistry();
        var toolRegistry = new ToolRegistry();
        var agentRegistry = new AgentRegistry(sp, []);
        agentRegistry.AddFactory(_ => new TestAIAgent(AgentName), null, AgentName, sp);

        chatClientRegistry.Register(AgentName, new TestChatClient(), null, null, providers.Length > 0 ? providers : null);

        var activity = new ResolveAgentContextActivity(chatClientRegistry, toolRegistry, agentRegistry, sp, NullLogger<ResolveAgentContextActivity>.Instance);
        return (activity, chatClientRegistry, toolRegistry);
    }

    private static TestWorkflowActivityContext MakeContext(string instanceId = "instance-1") => new(instanceId);

    private sealed class FakeContextProvider : AIContextProvider
    {
        public string? Instructions { get; set; }
        public IEnumerable<ChatMessage>? Messages { get; set; }
        public IEnumerable<AITool>? Tools { get; set; }
        public Exception? ThrowOnInvoking { get; set; }
        public Action<InvokingContext>? OnInvoking { get; set; }

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
        {
            OnInvoking?.Invoke(context);

            if (ThrowOnInvoking is not null)
            {
                throw ThrowOnInvoking;
            }

            return ValueTask.FromResult(new AIContext
            {
                Instructions = Instructions,
                Messages = Messages,
                Tools = Tools
            });
        }

        protected override ValueTask StoreAIContextAsync(InvokedContext context, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
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
