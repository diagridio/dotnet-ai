// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Diagnostics;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ExecuteToolActivityTests
{
    private const string AgentName = "test-agent";

    [Fact]
    public async Task RunAsync_RegisteredTool_InvokesToolAndReturnsSerializedResult()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        registry.Register(AgentName, AIFunctionFactory.Create((int value) => value + 1, name: "increment"));
        var activity = BuildActivity(registry, accessor);

        var output = await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "increment", "call-1", "{\"value\":41}"));

        Assert.Equal("call-1", output.CallId);
        Assert.Equal("increment", output.FunctionName);
        Assert.Equal("42", output.ResultJson);
        Assert.Null(output.Error);
    }

    [Fact]
    public async Task RunAsync_RegisteredTool_SetsAmbientContextDuringInvocation()
    {
        var accessor = new DaprAgentContextAccessor();
        var observedWorkflowId = string.Empty;
        var registry = new ToolRegistry();
        registry.Register(
            AgentName,
            AIFunctionFactory.Create(
                () =>
                {
                    observedWorkflowId = accessor.Current?.CurrentWorkflowInstanceId;
                    return "ok";
                },
                name: "capture_context"));
        var activity = BuildActivity(registry, accessor);

        await activity.RunAsync(
            MakeContext("workflow-42"),
            new ExecuteToolInput(AgentName, "capture_context", "call-1", "{}"));

        Assert.Equal("workflow-42", observedWorkflowId);
    }

    [Fact]
    public async Task RunAsync_RegisteredTool_ClearsAmbientContextAfterInvocation()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        registry.Register(AgentName, AIFunctionFactory.Create(() => "ok", name: "tool"));
        var activity = BuildActivity(registry, accessor);

        await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "tool", "call-1", "{}"));

        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task RunAsync_MissingTool_ThrowsHelpfulException()
    {
        var activity = BuildActivity(new ToolRegistry(), new DaprAgentContextAccessor());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.RunAsync(
                MakeContext(),
                new ExecuteToolInput(AgentName, "missing", "call-1", "{}")));

        Assert.Contains("Tool 'missing' not found", ex.Message);
        Assert.Contains(AgentName, ex.Message);
    }

    [Fact]
    public async Task RunAsync_ToolThrows_ClearsAmbientContextAndRethrows()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        Func<string> throwTool = () => throw new InvalidOperationException("tool failed");
        registry.Register(
            AgentName,
            AIFunctionFactory.Create(throwTool, name: "failing"));
        var activity = BuildActivity(registry, accessor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.RunAsync(
                MakeContext(),
                new ExecuteToolInput(AgentName, "failing", "call-1", "{}")));

        Assert.Equal("tool failed", ex.Message);
        Assert.Null(accessor.Current);
    }

    private static ExecuteToolActivity BuildActivity(ToolRegistry toolRegistry, IDaprAgentContextAccessor accessor)
    {
        var serviceProvider = new EmptyServiceProvider();
        var agentRegistry = new AgentRegistry(serviceProvider, []);
        agentRegistry.AddFactory(_ => new TestAIAgent(AgentName), null, AgentName, serviceProvider);

        return new ExecuteToolActivity(
            toolRegistry,
            agentRegistry,
            accessor,
            workflowClient: null!,
            serviceProvider,
            NullLogger<ExecuteToolActivity>.Instance);
    }

    private static TestWorkflowActivityContext MakeContext(string instanceId = "instance-1") =>
        new(instanceId);
    
    [Fact]
    public async Task RunAsync_AddsAgentAndToolBaggageToCurrentActivity()
    {
        const string agentName = "tool-agent";
        const string toolName = "lookup";
        const string callId = "call-1";

        var serviceProvider = new EmptyServiceProvider();
        var toolRegistry = new ToolRegistry();
        var function = AIFunctionFactory.Create(
            () => "ok",
            name: toolName,
            description: "Looks up test data.");
        toolRegistry.Register(agentName, function);

        var activity = new ExecuteToolActivity(
            toolRegistry,
            new AgentRegistry(serviceProvider, []),
            new DaprAgentContextAccessor(),
            workflowClient: null!,
            serviceProvider,
            NullLogger<ExecuteToolActivity>.Instance);

        using var current = new Activity("test").Start();

        await activity.RunAsync(
            new TestWorkflowActivityContext("workflow-1"),
            new ExecuteToolInput(agentName, toolName, callId, "{}"));

        Assert.Equal(agentName, current.GetBaggageItem(AgentTelemetryBaggage.AgentNameKey));
        Assert.Equal(AgentTelemetryBaggage.ToolOperation, current.GetBaggageItem(AgentTelemetryBaggage.AgentOperationKey));
        Assert.Equal(toolName, current.GetBaggageItem(AgentTelemetryBaggage.ToolNameKey));
        Assert.Equal(callId, current.GetBaggageItem(AgentTelemetryBaggage.ToolCallIdKey));
    }

    [Fact]
    public async Task RunAsync_AddsCustomBaggageAndAllowsFrameworkKeyOverrides()
    {
        const string agentName = "tool-agent";
        const string toolName = "lookup";
        const string callId = "call-1";

        var serviceProvider = new EmptyServiceProvider();
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(agentName, AIFunctionFactory.Create(() => "ok", name: toolName));

        var activity = new ExecuteToolActivity(
            toolRegistry,
            new AgentRegistry(serviceProvider, []),
            new DaprAgentContextAccessor(),
            workflowClient: null!,
            serviceProvider,
            NullLogger<ExecuteToolActivity>.Instance);

        using var current = new Activity("test").Start();

        await activity.RunAsync(
            new TestWorkflowActivityContext("workflow-1"),
            new ExecuteToolInput(
                agentName,
                toolName,
                callId,
                "{}",
                new Dictionary<string, string?>
                {
                    [AgentTelemetryBaggage.AgentOperationKey] = "override-operation",
                    ["tenant.id"] = "tenant-1"
                }));

        Assert.Equal(agentName, current.GetBaggageItem(AgentTelemetryBaggage.AgentNameKey));
        Assert.Equal("override-operation", current.GetBaggageItem(AgentTelemetryBaggage.AgentOperationKey));
        Assert.Equal(toolName, current.GetBaggageItem(AgentTelemetryBaggage.ToolNameKey));
        Assert.Equal(callId, current.GetBaggageItem(AgentTelemetryBaggage.ToolCallIdKey));
        Assert.Equal("tenant-1", current.GetBaggageItem("tenant.id"));
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
