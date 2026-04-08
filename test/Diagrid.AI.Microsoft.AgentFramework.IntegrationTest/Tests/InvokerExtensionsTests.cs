// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates the convenience extension methods on
/// <see cref="Diagrid.AI.Microsoft.AgentFramework.Hosting.DaprAgentInvokerExtensions"/>:
/// string-name overloads of <c>RunAgentAsync</c>, the <c>GetAgent(name, chatClientKey)</c> overload,
/// and the <c>RunAgentAndDeserializeAsync&lt;T, TCategory&gt;</c> / explicit-logger variants.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class InvokerExtensionsTests(DaprFixture fixture)
{
    // ── RunAgentAsync by agent name ──────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_ByAgentName_ReturnsExpectedResponse()
    {
        // Uses DaprAgentInvokerExtensions.RunAgentAsync(this IDaprAgentInvoker, string agentName, ...)
        var response = await fixture.Invoker.RunAgentAsync("EchoAgent", "hello via name overload");

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_ByAgentName_MultipleDistinctAgents_ReturnCorrectResponses()
    {
        var greetResponse = await fixture.Invoker.RunAgentAsync("GreetingAgent", "hi");
        var echoResponse  = await fixture.Invoker.RunAgentAsync("EchoAgent",     "hi");

        Assert.Equal("Hello!",                greetResponse.Text);
        Assert.Equal("Hello from EchoAgent!", echoResponse.Text);
    }

    // ── GetAgent with chat-client key (extension overload) ───────────────────

    [Fact]
    public async Task GetAgent_Extension_WithChatClientKey_ResolvesKeyedAgent()
    {
        // Uses DaprAgentInvokerExtensions.GetAgent(this IDaprAgentInvoker, string, string?)
        var alpha = fixture.Invoker.GetAgent("AlphaAgent", "chat-key-alpha");
        var beta  = fixture.Invoker.GetAgent("BetaAgent",  "chat-key-beta");

        var alphaResp = await fixture.Invoker.RunAgentAsync(alpha, "keyed question");
        var betaResp  = await fixture.Invoker.RunAgentAsync(beta,  "keyed question");

        Assert.Equal("Alpha response", alphaResp.Text);
        Assert.Equal("Beta response",  betaResp.Text);
    }

    [Fact]
    public async Task GetAgent_Extension_WithNullChatClientKey_ResolvesNonKeyedAgent()
    {
        // chatClientKey = null should behave the same as IDaprAgentInvoker.GetAgent(name)
        var agent    = fixture.Invoker.GetAgent("EchoAgent", chatClientKey: null);
        var response = await fixture.Invoker.RunAgentAsync(agent, "test");

        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    // ── RunAgentAndDeserializeAsync<T> via name-based extension ──────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_ByAgentName_ReturnsTypedObject()
    {
        // Uses DaprAgentInvokerExtensions.RunAgentAndDeserializeAsync<T>(this IDaprAgentInvoker, string, ...)
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            "CapitalAgent", message: "What is the capital of France?");

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
        Assert.Equal(0.99, result.Confidence, precision: 5);
    }

    // ── RunAgentAndDeserializeAsync<T, TCategory> ────────────────────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithLoggerCategory_ReturnsTypedObject()
    {
        // Uses IDaprAgentInvoker.RunAgentAndDeserializeAsync<T, TCategory>(...)
        // TCategory drives which ILogger<TCategory> is created internally.
        var agent  = fixture.Invoker.GetAgent("CapitalAgent");
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer, InvokerExtensionsTests>(
            agent, message: "What is the capital of France?");

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
    }

    // ── RunAgentAndDeserializeAsync<T> with explicit ILogger ─────────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithExplicitLogger_ReturnsTypedObject()
    {
        // Uses IDaprAgentInvoker.RunAgentAndDeserializeAsync<T>(IDaprAIAgent, ILogger, message, ...)
        var agent  = fixture.Invoker.GetAgent("CapitalAgent");
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            agent,
            NullLogger.Instance,
            message: "capital question");

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
    }

    // ── RunAgentAndDeserializeAsync<T> via name-based extension with logger ──

    [Fact]
    public async Task RunAgentAndDeserializeAsync_ByAgentName_WithExplicitLogger_ReturnsTypedObject()
    {
        // Uses DaprAgentInvokerExtensions.RunAgentAndDeserializeAsync<T>(this IDaprAgentInvoker, string agentName, ILogger, ...)
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            "CapitalAgent",
            NullLogger.Instance,
            message: "What is the capital of France?");

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
    }
}
