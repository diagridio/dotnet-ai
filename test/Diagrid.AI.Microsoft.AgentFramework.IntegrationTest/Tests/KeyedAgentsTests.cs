// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates keyed-agent registration: agents registered with
/// <see cref="Diagrid.AI.Microsoft.AgentFramework.Hosting.IAgentsBuilder.WithAgent(string, Func{IServiceProvider, Microsoft.Agents.AI.AIAgent})"/>
/// (i.e. associated with a specific chat-client key) can be resolved and invoked correctly.
/// This mirrors the <c>KeyedAgentInvokerDemo</c> example which uses multiple Dapr Conversation
/// components.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class KeyedAgentsTests(DaprFixture fixture)
{
    [Fact]
    public async Task AlphaAgent_Returns_AlphaResponse()
    {
        // AlphaAgent is registered with chat-client key "chat-key-alpha".
        // Because its name is unique, GetAgent("AlphaAgent") resolves it without needing the key.
        var agent    = fixture.Invoker.GetAgent("AlphaAgent");
        var response = await fixture.Invoker.RunAgentAsync(agent, "question for alpha");

        Assert.Equal("Alpha response", response.Text);
    }

    [Fact]
    public async Task BetaAgent_Returns_BetaResponse()
    {
        var agent    = fixture.Invoker.GetAgent("BetaAgent");
        var response = await fixture.Invoker.RunAgentAsync(agent, "question for beta");

        Assert.Equal("Beta response", response.Text);
    }

    [Fact]
    public async Task AlphaAndBeta_AreIndependent_ResponsesDoNotCross()
    {
        var alpha = fixture.Invoker.GetAgent("AlphaAgent");
        var beta  = fixture.Invoker.GetAgent("BetaAgent");

        var alphaResp = await fixture.Invoker.RunAgentAsync(alpha, "what are you?");
        var betaResp  = await fixture.Invoker.RunAgentAsync(beta,  "what are you?");

        Assert.Equal("Alpha response", alphaResp.Text);
        Assert.Equal("Beta response",  betaResp.Text);
        Assert.NotEqual(alphaResp.Text, betaResp.Text);
    }

    [Fact]
    public async Task KeyedAgents_AndNonKeyedAgents_CoexistWithoutAmbiguity()
    {
        // EchoAgent (no chat-client key), AlphaAgent (key="chat-key-alpha"),
        // and BetaAgent (key="chat-key-beta") must all resolve without error.
        var echo  = fixture.Invoker.GetAgent("EchoAgent");
        var alpha = fixture.Invoker.GetAgent("AlphaAgent");
        var beta  = fixture.Invoker.GetAgent("BetaAgent");

        var results = await Task.WhenAll(
            fixture.Invoker.RunAgentAsync(echo,  "msg"),
            fixture.Invoker.RunAgentAsync(alpha, "msg"),
            fixture.Invoker.RunAgentAsync(beta,  "msg"));
        var echoResp  = results[0];
        var alphaResp = results[1];
        var betaResp  = results[2];

        Assert.Equal("Hello from EchoAgent!", echoResp.Text);
        Assert.Equal("Alpha response",         alphaResp.Text);
        Assert.Equal("Beta response",          betaResp.Text);
    }
}
