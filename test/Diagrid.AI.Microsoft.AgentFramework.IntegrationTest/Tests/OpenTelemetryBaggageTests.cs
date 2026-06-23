// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Diagnostics;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates that agent and tool context is attached to the OpenTelemetry
/// activity baggage visible during real Dapr Workflow activity execution.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class OpenTelemetryBaggageTests(DaprFixture fixture)
{
    private const string AgentNameKey = "agent.name";
    private const string AgentOperationKey = "agent.operation";
    private const string ToolNameKey = "tool.name";
    private const string ToolCallIdKey = "tool.call_id";

    [Fact]
    public async Task RunAgentAsync_RecordsAgentBaggage_OnLlmActivity()
    {
        fixture.TelemetryBaggageRecorder.Reset();

        using var parent = StartParentActivity();

        var agent = fixture.Invoker.GetAgent(TelemetryBaggageMockChatClient.AgentName);
        var response = await fixture.Invoker.RunAgentAsync(agent, "capture baggage");

        Assert.Equal("telemetry complete", response.Text);

        var baggage = await WaitForBaggageAsync(
            () => fixture.TelemetryBaggageRecorder.LlmBaggage,
            item => item.TryGetValue(AgentNameKey, out var agentName) &&
                    agentName == TelemetryBaggageMockChatClient.AgentName &&
                    item.TryGetValue(AgentOperationKey, out var operation) &&
                    operation == "llm");

        Assert.Equal(TelemetryBaggageMockChatClient.AgentName, baggage[AgentNameKey]);
        Assert.Equal("llm", baggage[AgentOperationKey]);
    }

    [Fact]
    public async Task RunAgentAsync_WithTool_RecordsAgentAndToolBaggage_OnToolActivity()
    {
        fixture.TelemetryBaggageRecorder.Reset();

        using var parent = StartParentActivity();

        var agent = fixture.Invoker.GetAgent(TelemetryBaggageMockChatClient.AgentName);
        var response = await fixture.Invoker.RunAgentAsync(agent, "please use the tool");

        Assert.Equal("telemetry complete", response.Text);

        var baggage = await WaitForBaggageAsync(
            () => fixture.TelemetryBaggageRecorder.ToolBaggage,
            item => item.TryGetValue(AgentNameKey, out var agentName) &&
                    agentName == TelemetryBaggageMockChatClient.AgentName &&
                    item.TryGetValue(AgentOperationKey, out var operation) &&
                    operation == "tool" &&
                    item.TryGetValue(ToolNameKey, out var toolName) &&
                    toolName == TelemetryBaggageMockChatClient.ToolName &&
                    item.TryGetValue(ToolCallIdKey, out var callId) &&
                    callId == TelemetryBaggageMockChatClient.ToolCallId);

        Assert.Equal(TelemetryBaggageMockChatClient.AgentName, baggage[AgentNameKey]);
        Assert.Equal("tool", baggage[AgentOperationKey]);
        Assert.Equal(TelemetryBaggageMockChatClient.ToolName, baggage[ToolNameKey]);
        Assert.Equal(TelemetryBaggageMockChatClient.ToolCallId, baggage[ToolCallIdKey]);
    }

    private static Activity StartParentActivity()
    {
        var activity = new Activity("otel-baggage-integration-test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }

    private static async Task<IReadOnlyDictionary<string, string>> WaitForBaggageAsync(
        Func<IReadOnlyCollection<IReadOnlyDictionary<string, string>>> getBaggage,
        Func<IReadOnlyDictionary<string, string>, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var item in getBaggage())
            {
                if (predicate(item))
                {
                    return item;
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        var captured = getBaggage();
        throw new TimeoutException(
            "Timed out waiting for expected OpenTelemetry baggage. " +
            $"Captured baggage: {string.Join(", ", captured.Select(FormatBaggage))}");
    }

    private static string FormatBaggage(IReadOnlyDictionary<string, string> baggage) =>
        $"[{string.Join(", ", baggage.Select(kv => $"{kv.Key}={kv.Value}"))}]";
}
