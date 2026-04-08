// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates that multiple <see cref="Microsoft.Agents.AI.AIAgent"/> instances can be registered
/// under distinct names and invoked independently through <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker"/>.
/// This mirrors the multi-agent setup shown in the <c>RouterDemo</c> and <c>EmailGateDemo</c> examples.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class MultipleAgentsTests(DaprFixture fixture)
{
    [Fact]
    public async Task EachAgent_Returns_ItsOwnDistinctResponse()
    {
        var greetingAgent  = fixture.Invoker.GetAgent("GreetingAgent");
        var farewellAgent  = fixture.Invoker.GetAgent("FarewellAgent");
        var echoAgent      = fixture.Invoker.GetAgent("EchoAgent");

        var greetingResponse  = await fixture.Invoker.RunAgentAsync(greetingAgent,  "hi");
        var farewellResponse  = await fixture.Invoker.RunAgentAsync(farewellAgent,  "bye");
        var echoResponse      = await fixture.Invoker.RunAgentAsync(echoAgent,      "echo");

        Assert.Equal("Hello!",                  greetingResponse.Text);
        Assert.Equal("Goodbye!",                farewellResponse.Text);
        Assert.Equal("Hello from EchoAgent!",   echoResponse.Text);
    }

    [Fact]
    public async Task InvokingAgentsInParallel_AllResponsesAreCorrect()
    {
        var greetingAgent = fixture.Invoker.GetAgent("GreetingAgent");
        var farewellAgent = fixture.Invoker.GetAgent("FarewellAgent");

        // Run both agents concurrently – Dapr Workflow handles each as an independent workflow instance.
        var results = await Task.WhenAll(
            fixture.Invoker.RunAgentAsync(greetingAgent, "parallel-hello"),
            fixture.Invoker.RunAgentAsync(farewellAgent, "parallel-bye"));
        var greetingResp = results[0];
        var farewellResp = results[1];

        Assert.Equal("Hello!",    greetingResp.Text);
        Assert.Equal("Goodbye!",  farewellResp.Text);
    }

    [Fact]
    public async Task SameAgentInvokedRepeatedly_ReturnsSameResponse()
    {
        var agent = fixture.Invoker.GetAgent("GreetingAgent");

        var first  = await fixture.Invoker.RunAgentAsync(agent, "run-1");
        var second = await fixture.Invoker.RunAgentAsync(agent, "run-2");
        var third  = await fixture.Invoker.RunAgentAsync(agent, "run-3");

        Assert.Equal(first.Text,  second.Text);
        Assert.Equal(second.Text, third.Text);
    }

    [Fact]
    public async Task AgentRegistry_ContainsAllRegisteredAgentNames()
    {
        // Verify that the registry exposes the names of all registered agents.
        var registry = fixture.Invoker as Diagrid.AI.Microsoft.AgentFramework.Hosting.DaprAgentInvoker;

        // Indirectly confirm agents are registered by invoking each expected agent.
        var expectedAgents = new[] { "EchoAgent", "CapitalAgent", "GreetingAgent", "FarewellAgent" };

        foreach (var name in expectedAgents)
        {
            var agent    = fixture.Invoker.GetAgent(name);
            var response = await fixture.Invoker.RunAgentAsync(agent, "ping");

            Assert.NotNull(response);
            Assert.False(string.IsNullOrEmpty(response.Text),
                $"Agent '{name}' returned an empty response.");
        }
    }
}
