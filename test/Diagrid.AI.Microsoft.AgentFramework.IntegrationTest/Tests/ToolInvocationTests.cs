// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates that tool invocations are dispatched as separate Dapr Workflow activities
/// (via <c>ToolRunWorkflow</c> / <c>InvokeToolActivity</c>) rather than being executed
/// inline within the parent <c>InvokeAgentActivity</c>.
///
/// <para>
/// The agent under test (<c>ToolInvocationAgent</c>) is backed by a
/// <c>ToolCallMockChatClient</c>: on the first LLM turn it requests the
/// <c>process_input</c> tool; on the second turn (after receiving the tool result) it
/// returns a final text answer embedding the result.
/// </para>
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class ToolInvocationTests(DaprFixture fixture)
{
    private const string AgentName = "ToolInvocationAgent";

    [Fact]
    public async Task RunAgentAsync_WithTool_CompletesSuccessfully()
    {
        var agent    = fixture.Invoker.GetAgent(AgentName);
        var response = await fixture.Invoker.RunAgentAsync(agent, "please use the tool");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public async Task RunAgentAsync_WithTool_ResponseContainsToolResult()
    {
        var agent    = fixture.Invoker.GetAgent(AgentName);
        var response = await fixture.Invoker.RunAgentAsync(agent, "please use the tool");

        // The mock chat client embeds the tool's return value in the final answer.
        // process_input("test-value") → "processed:test-value", which is serialised
        // to JSON ("\"processed:test-value\"") and back, then embedded as:
        //   "Tool returned: processed:test-value"
        Assert.NotNull(response.Text);
        Assert.Contains("processed:test-value", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_WithTool_ToolFunctionIsInvoked()
    {
        fixture.ToolTracker.Reset();

        var agent = fixture.Invoker.GetAgent(AgentName);
        await fixture.Invoker.RunAgentAsync(agent, "please use the tool");

        Assert.True(fixture.ToolTracker.InvocationCount > 0,
            "Expected the process_input tool to have been invoked at least once.");
    }

    [Fact]
    public async Task RunAgentAsync_WithTool_MultipleSequentialInvocations_AllSucceed()
    {
        const int iterations = 3;

        for (var i = 0; i < iterations; i++)
        {
            var agent    = fixture.Invoker.GetAgent(AgentName);
            var response = await fixture.Invoker.RunAgentAsync(agent, $"run {i}");

            Assert.NotNull(response);
            Assert.Contains("processed:test-value", response.Text);
        }
    }

    [Fact]
    public async Task RunAgentAsync_WithTool_ConcurrentInvocations_AllReturnExpectedResult()
    {
        var agent = fixture.Invoker.GetAgent(AgentName);

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => fixture.Invoker.RunAgentAsync(agent, "concurrent tool call"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
        {
            Assert.NotNull(r);
            Assert.Contains("processed:test-value", r.Text);
        });
    }
}
