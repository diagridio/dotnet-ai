// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates that <see cref="Microsoft.Agents.AI.AgentRunOptions"/> values passed to
/// <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker.RunAgentAsync"/>
/// survive the Dapr Workflow serialization / deserialization round-trip and are forwarded to the
/// underlying <see cref="Microsoft.Agents.AI.AIAgent"/> without error.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class AgentRunOptionsTests(DaprFixture fixture)
{
    // ── AgentRunOptions propagation ───────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithNonNullOptions_Succeeds()
    {
        // An empty AgentRunOptions must survive JSON round-trip through Dapr Workflow
        // and reach AIAgent.RunAsync without causing a serialization or runtime error.
        var agent   = fixture.Invoker.GetAgent("EchoAgent");
        var options = new AgentRunOptions();

        var response = await fixture.Invoker.RunAgentAsync(
            agent, message: "test with options", options: options);

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_WithNullOptions_Succeeds()
    {
        // Null options is the common case; verifies it is handled as a baseline.
        var agent    = fixture.Invoker.GetAgent("EchoAgent");
        var response = await fixture.Invoker.RunAgentAsync(
            agent, message: "test with null options", options: null);

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_WithNonNullOptions_ProducesIdenticalResponseToNullOptions()
    {
        // Passing AgentRunOptions vs. null must not change the functional response from a
        // TestAIAgent that ignores options.
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        var withOptions    = await fixture.Invoker.RunAgentAsync(agent, "msg", options: new AgentRunOptions());
        var withoutOptions = await fixture.Invoker.RunAgentAsync(agent, "msg", options: null);

        Assert.Equal(withOptions.Text, withoutOptions.Text);
    }

    // ── Options propagation via the name-based extension overload ─────────────

    [Fact]
    public async Task RunAgentAsync_ByName_WithNonNullOptions_Succeeds()
    {
        var response = await fixture.Invoker.RunAgentAsync(
            "GreetingAgent", message: "greet with options", options: new AgentRunOptions());

        Assert.NotNull(response);
        Assert.Equal("Hello!", response.Text);
    }

    // ── Options + typed deserialization ──────────────────────────────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithNonNullOptions_ReturnsTypedObject()
    {
        var agent   = fixture.Invoker.GetAgent("CapitalAgent");
        var options = new AgentRunOptions();

        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            agent, message: "capital with options", options: options);

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
    }
}
