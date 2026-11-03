// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

/// <summary>
/// The helpers the builder extensions and the runtime reach <see cref="DaprAgentsBuilder"/> through.
/// </summary>
public sealed class DaprAgentsBuilderAccessTests
{
    [Fact]
    public void From_DaprAgentsBuilder_ReturnsIt()
    {
        var builder = (DaprAgentsBuilder)new ServiceCollection().AddDaprAgents();

        Assert.Same(builder, DaprAgentsBuilder.From(builder, "do this"));
    }

    [Fact]
    public void From_OtherBuilder_ThrowsNamingWhatWasNeeded()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DaprAgentsBuilder.From(Mock.Of<IAgentsBuilder>(), "expose an IServiceCollection"));

        Assert.Equal("The agents builder does not expose an IServiceCollection.", ex.Message);
    }

    [Fact]
    public void BuilderExtensions_OnAnotherBuilder_KeepTheirMessages()
    {
        var other = Mock.Of<IAgentsBuilder>();

        Assert.Equal("The agents builder does not support explicit registrations.",
            Assert.Throws<InvalidOperationException>(
                () => other.WithAgent(new AgentFactoryRegistration(_ => new TestAIAgent("a")))).Message);
        Assert.Equal("The agents builder does not expose an IServiceCollection.",
            Assert.Throws<InvalidOperationException>(() => other.WithLlmTimeout(TimeSpan.FromSeconds(1))).Message);
        Assert.Equal("The agents builder does not expose its Dapr Workflow builder.",
            Assert.Throws<InvalidOperationException>(() => other.WithBinaryStorePayloads("store")).Message);
    }

    [Fact]
    public void IsDaprChatClient_FindsItThroughWrappers()
    {
        var dapr = new DaprChatClient();

        Assert.True(DaprAgentsBuilder.IsDaprChatClient(dapr));
        Assert.True(DaprAgentsBuilder.IsDaprChatClient(new ToolResultCompatibilityChatClient(dapr)));
        Assert.True(DaprAgentsBuilder.IsDaprChatClient(new PassThroughChatClient(new ToolResultCompatibilityChatClient(dapr))));
        Assert.False(DaprAgentsBuilder.IsDaprChatClient(new PassThroughChatClient(new OtherChatClient())));
    }

    private sealed class PassThroughChatClient(IChatClient inner) : DelegatingChatClient(inner);

    private class OtherChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // Matched by type name, like the real Dapr.AI.Microsoft.Extensions client.
    private sealed class DaprChatClient : OtherChatClient;
}
