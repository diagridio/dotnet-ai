using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

public sealed class DaprAgentInvokerExtensionsTests
{
    [Fact]
    public async Task RunAgentAsync_ForwardsParameters()
    {
        var invoker = new FakeInvoker();

        var response = await invoker.RunAgentAsync("alpha", message: "hello", chatClientKey: "key");

        Assert.NotNull(response);
        Assert.NotNull(invoker.LastAgent);
        Assert.Equal("alpha", invoker.LastAgent!.Name);
        Assert.Equal("hello", invoker.LastMessage);
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_ForwardsParameters()
    {
        var invoker = new FakeInvoker();

        var result = await invoker.RunAgentAndDeserializeAsync<TestPayload>("beta", NullLogger.Instance, message: "hi");

        Assert.NotNull(result);
        Assert.Equal("from-invoker", result!.Value);
        Assert.NotNull(invoker.LastAgent);
        Assert.Equal("beta", invoker.LastAgent!.Name);
        Assert.Equal("hi", invoker.LastMessage);
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithCategory_ForwardsParameters()
    {
        var invoker = new FakeInvoker();

        var result = await invoker.RunAgentAndDeserializeAsync<TestPayload, DaprAgentInvokerExtensionsTests>("gamma", message: "msg");

        Assert.NotNull(result);
        Assert.Equal("from-invoker", result!.Value);
    }

    private sealed class FakeInvoker : IDaprAgentInvoker
    {
        public IDaprAIAgent? LastAgent { get; private set; }
        public string? LastMessage { get; private set; }

        public IDaprAIAgent GetAgent(string agentName) => new TestAgentReference(agentName);

        public Task<AgentRunResponse> RunAgentAsync(IDaprAIAgent agent, string? message = null, AgentThread? thread = null,
            AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastAgent = agent;
            LastMessage = message;
            return Task.FromResult(AgentRunResponseFactory.CreateWithText("{}"));
        }

        public Task<T?> RunAgentAndDeserializeAsync<T>(IDaprAIAgent agent, ILogger logger,
            string? message = null, AgentThread? thread = null, AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastAgent = agent;
            LastMessage = message;
            object? payload = typeof(T) == typeof(TestPayload) ? new TestPayload("from-invoker") : default(T);
            return Task.FromResult((T?)payload);
        }

        public Task<T?> RunAgentAndDeserializeAsync<T>(IDaprAIAgent agent, string? message = null, AgentThread? thread = null,
            AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastAgent = agent;
            LastMessage = message;
            object? payload = typeof(T) == typeof(TestPayload) ? new TestPayload("from-invoker") : default(T);
            return Task.FromResult((T?)payload);
        }

        public Task<T?> RunAgentAndDeserializeAsync<T, TCategory>(IDaprAIAgent agent, string? message = null,
            AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastAgent = agent;
            LastMessage = message;
            object? payload = typeof(T) == typeof(TestPayload) ? new TestPayload("from-invoker") : default(T);
            return Task.FromResult((T?)payload);
        }
    }

    private sealed class TestAgentReference(string name) : IDaprAIAgent
    {
        public string Name { get; } = name;
    }
}
