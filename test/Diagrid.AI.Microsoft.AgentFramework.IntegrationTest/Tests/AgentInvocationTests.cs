// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Net;
using System.Net.Http.Json;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates that <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker"/>
/// can schedule and complete a workflow-backed agent run, mirroring the <c>AgentInvokerDemo</c>
/// <c>POST /ask</c> endpoint.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class AgentInvocationTests(DaprFixture fixture)
{
    // ── Via HTTP (mirrors AgentInvokerDemo) ──────────────────────────────────

    [Fact]
    public async Task Post_Ask_Returns200_WithAgentResponseText()
    {
        var response = await fixture.Client.PostAsJsonAsync("/ask",
            new AskRequest("What is 2+2?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AskResponse>(
            IntegrationTestJsonContext.Default.AskResponse);

        Assert.NotNull(body);
        Assert.Equal("Hello from EchoAgent!", body!.Response);
    }

    [Fact]
    public async Task Post_Ask_WithExplicitAgentName_Routes_To_CorrectAgent()
    {
        // EchoAgent and GreetingAgent return different responses.
        var echoResponse = await fixture.Client.PostAsJsonAsync("/ask",
            new AskRequest("hello", AgentName: "EchoAgent"));
        var greetResponse = await fixture.Client.PostAsJsonAsync("/ask",
            new AskRequest("hello", AgentName: "GreetingAgent"));

        var echo   = await echoResponse.Content.ReadFromJsonAsync<AskResponse>(
            IntegrationTestJsonContext.Default.AskResponse);
        var greet  = await greetResponse.Content.ReadFromJsonAsync<AskResponse>(
            IntegrationTestJsonContext.Default.AskResponse);

        Assert.Equal("Hello from EchoAgent!", echo!.Response);
        Assert.Equal("Hello!", greet!.Response);
    }

    [Fact]
    public async Task Post_Ask_MultipleSequentialInvocations_AllSucceed()
    {
        const int iterations = 3;

        for (var i = 0; i < iterations; i++)
        {
            var response = await fixture.Client.PostAsJsonAsync("/ask",
                new AskRequest($"Invocation {i}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── Via IDaprAgentInvoker directly ───────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_Returns_ExpectedResponseText()
    {
        var agent    = fixture.Invoker.GetAgent("EchoAgent");
        var response = await fixture.Invoker.RunAgentAsync(agent, "test message");

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_NullMessage_ThrowsOrFails()
    {
        // AIAgent.RunAsync rejects null/whitespace messages (ArgumentException from
        // Microsoft.Agents.AI). The framework surfaces this as an InvalidOperationException
        // because the workflow activity fails.
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Invoker.RunAgentAsync(agent, message: null));
    }

    [Fact]
    public async Task RunAgentAsync_ConcurrentInvocations_AllComplete()
    {
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        var tasks = Enumerable.Range(0, 5)
            .Select(i => fixture.Invoker.RunAgentAsync(agent, $"concurrent-{i}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
        {
            Assert.NotNull(r);
            Assert.Equal("Hello from EchoAgent!", r.Text);
        });
    }

    [Fact]
    public async Task GetAgent_UnknownName_WorkflowFails_WithDescriptiveException()
    {
        var agent = fixture.Invoker.GetAgent("NonExistentAgent");

        // The activity throws InvalidOperationException which the invoker surfaces as
        // "Agent workflow '...' completed with status 'Failed'. <inner message>".
        // The inner message from AgentRegistry includes the agent name.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Invoker.RunAgentAsync(agent, "test"));

        Assert.Contains("Failed", ex.Message);
    }
}
