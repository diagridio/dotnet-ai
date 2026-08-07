// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ChatClientRegistryTests
{
    private const string AgentName = "test-agent";

    [Fact]
    public void Register_StoresChatClientInstructionsAndTools()
    {
        var registry = new ChatClientRegistry();
        var client = new TestChatClient();
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "ok", name: "probe") };

        registry.Register(AgentName, client, "Be helpful.", tools);

        var config = registry.Get(AgentName);
        Assert.NotNull(config);
        Assert.Same(client, config!.ChatClient);
        Assert.Equal("Be helpful.", config.Instructions);
        Assert.Same(tools, config.Tools);
        Assert.Null(config.ContextProviders);
    }

    [Fact]
    public void Get_UnregisteredAgent_ReturnsNull()
    {
        var registry = new ChatClientRegistry();

        Assert.Null(registry.Get("missing"));
    }

    [Fact]
    public void Contains_ReflectsRegisteredAgents()
    {
        var registry = new ChatClientRegistry();
        Assert.False(registry.Contains(AgentName));

        registry.Register(AgentName, new TestChatClient(), null, null);

        Assert.True(registry.Contains(AgentName));
    }

    [Fact]
    public void Register_WithContextProviders_StoresThem()
    {
        var registry = new ChatClientRegistry();
        var provider = new TestContextProvider();

        registry.Register(AgentName, new TestChatClient(), null, null, [provider]);

        var config = registry.Get(AgentName);
        Assert.NotNull(config!.ContextProviders);
        Assert.Same(provider, Assert.Single(config.ContextProviders!));
    }

    [Fact]
    public void RegisterContextProviders_BeforeRegister_IsMergedInOnRegister()
    {
        // Mirrors WithContextProviders/WithSkills, called before the agent factory materializes.
        var registry = new ChatClientRegistry();
        var provider = new TestContextProvider();

        registry.RegisterContextProviders(AgentName, [provider]);
        registry.Register(AgentName, new TestChatClient(), null, null);

        var config = registry.Get(AgentName);
        Assert.Same(provider, Assert.Single(config!.ContextProviders!));
    }

    [Fact]
    public void RegisterContextProviders_MultipleCallsForSameAgent_Accumulate()
    {
        var registry = new ChatClientRegistry();
        var providerA = new TestContextProvider();
        var providerB = new TestContextProvider();

        registry.RegisterContextProviders(AgentName, [providerA]);
        registry.RegisterContextProviders(AgentName, [providerB]);
        registry.Register(AgentName, new TestChatClient(), null, null);

        var config = registry.Get(AgentName);
        Assert.Equal(2, config!.ContextProviders!.Count);
        Assert.Contains(providerA, config.ContextProviders!);
        Assert.Contains(providerB, config.ContextProviders!);
    }

    [Fact]
    public void RegisterContextProviders_MergesWithExplicitlyPassedProviders_OnRegister()
    {
        var registry = new ChatClientRegistry();
        var pending = new TestContextProvider();
        var explicitProvider = new TestContextProvider();

        registry.RegisterContextProviders(AgentName, [pending]);
        registry.Register(AgentName, new TestChatClient(), null, null, [explicitProvider]);

        var config = registry.Get(AgentName);
        Assert.Equal(2, config!.ContextProviders!.Count);
        Assert.Contains(pending, config.ContextProviders!);
        Assert.Contains(explicitProvider, config.ContextProviders!);
    }

    [Fact]
    public void RegisterContextProviders_ConsumedOnce_DoesNotDuplicateOnReRegister()
    {
        var registry = new ChatClientRegistry();
        var provider = new TestContextProvider();

        registry.RegisterContextProviders(AgentName, [provider]);
        registry.Register(AgentName, new TestChatClient(), "v1", null);
        registry.Register(AgentName, new TestChatClient(), "v2", null); // e.g. re-materialization

        var config = registry.Get(AgentName);
        Assert.Equal("v2", config!.Instructions);
        Assert.Null(config.ContextProviders);
    }

    [Fact]
    public void Constructor_SeedsFromContextProviderRegistrations()
    {
        var provider = new TestContextProvider();
        var registry = new ChatClientRegistry([new ContextProviderRegistration(AgentName, [provider])]);

        registry.Register(AgentName, new TestChatClient(), null, null);

        var config = registry.Get(AgentName);
        Assert.Same(provider, Assert.Single(config!.ContextProviders!));
    }

    [Fact]
    public void Register_RejectsBlankAgentName()
    {
        var registry = new ChatClientRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(" ", new TestChatClient(), null, null));
    }

    [Fact]
    public void Register_RejectsNullChatClient()
    {
        var registry = new ChatClientRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(AgentName, null!, null, null));
    }

    [Fact]
    public void RegisterContextProviders_RejectsBlankAgentName()
    {
        var registry = new ChatClientRegistry();

        Assert.Throws<ArgumentException>(() => registry.RegisterContextProviders(" ", [new TestContextProvider()]));
    }

    private sealed class TestContextProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AIContext());

        protected override ValueTask StoreAIContextAsync(InvokedContext context, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
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
