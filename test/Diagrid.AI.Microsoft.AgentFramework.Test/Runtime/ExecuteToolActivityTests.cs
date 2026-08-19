// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Diagnostics;
using System.Text.Json;
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

    [Fact]
    public async Task RunAsync_RegisteredTool_SetsServicesOnFunctionArguments()
    {
        // Regression guard: MAF skill tools (read_skill_resource, run_skill_script) resolve
        // services via AIFunctionArguments.Services and throw ArgumentNullException without it.
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        var serviceProvider = new EmptyServiceProvider();
        IServiceProvider? capturedServices = null;
        registry.Register(
            AgentName,
            AIFunctionFactory.Create(
                (AIFunctionArguments args) =>
                {
                    capturedServices = args.Services;
                    return "ok";
                },
                name: "needs_services"));

        var activity = BuildActivity(registry, accessor, serviceProvider);

        await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "needs_services", "call-1", "{}"));

        Assert.Same(serviceProvider, capturedServices);
    }

    // =========================================================================
    // Approval-required tools (Microsoft.Extensions.AI.ApprovalRequiredAIFunction)
    // =========================================================================

    [Fact]
    public async Task RunAsync_ApprovalRequiredFunction_Approved_InvokesUnderlyingFunction()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        var invoked = false;
        var inner = AIFunctionFactory.Create(() =>
        {
            invoked = true;
            return "script output";
        }, name: "run_skill_script");
        registry.Register(AgentName, new ApprovalRequiredAIFunction(inner));

        var handler = new StubToolApprovalHandler(ToolApprovalDecision.Approve());
        var activity = BuildActivity(registry, accessor, approvalHandler: handler);

        var output = await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "run_skill_script", "call-1", "{}"));

        Assert.True(invoked);
        Assert.Equal("\"script output\"", output.ResultJson);
        Assert.Single(handler.Requests);
        Assert.Equal("run_skill_script", handler.Requests[0].ToolName);
        Assert.Equal("call-1", handler.Requests[0].CallId);
    }

    [Fact]
    public async Task RunAsync_ApprovalRequiredFunction_Denied_ReturnsDenialWithoutInvoking()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        var invoked = false;
        var inner = AIFunctionFactory.Create(() =>
        {
            invoked = true;
            return "should not run";
        }, name: "run_skill_script");
        registry.Register(AgentName, new ApprovalRequiredAIFunction(inner));

        var handler = new StubToolApprovalHandler(ToolApprovalDecision.Deny("not allowed"));
        var activity = BuildActivity(registry, accessor, approvalHandler: handler);

        var output = await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "run_skill_script", "call-1", "{}"));

        Assert.False(invoked);
        Assert.Null(output.Error);
        var payload = JsonSerializer.Deserialize<JsonElement>(output.ResultJson!);
        Assert.False(payload.GetProperty("approved").GetBoolean());
        Assert.Equal("not allowed", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RunAsync_ApprovalRequiredFunction_NoHandlerRegistered_DeniesByDefault()
    {
        // DenyingToolApprovalHandler is the default registered by AddDaprAgents() — fail closed.
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        var invoked = false;
        var inner = AIFunctionFactory.Create(() =>
        {
            invoked = true;
            return "should not run";
        }, name: "run_skill_script");
        registry.Register(AgentName, new ApprovalRequiredAIFunction(inner));

        var activity = BuildActivity(registry, accessor, approvalHandler: new DenyingToolApprovalHandler());

        var output = await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "run_skill_script", "call-1", "{}"));

        Assert.False(invoked);
        var payload = JsonSerializer.Deserialize<JsonElement>(output.ResultJson!);
        Assert.False(payload.GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_NonApprovalRequiredFunction_DoesNotConsultApprovalHandler()
    {
        var accessor = new DaprAgentContextAccessor();
        var registry = new ToolRegistry();
        registry.Register(AgentName, AIFunctionFactory.Create(() => "ok", name: "plain_tool"));

        var handler = new StubToolApprovalHandler(ToolApprovalDecision.Deny("irrelevant"));
        var activity = BuildActivity(registry, accessor, approvalHandler: handler);

        var output = await activity.RunAsync(
            MakeContext(),
            new ExecuteToolInput(AgentName, "plain_tool", "call-1", "{}"));

        Assert.Empty(handler.Requests);
        Assert.Equal("\"ok\"", output.ResultJson);
    }

    private static ExecuteToolActivity BuildActivity(
        ToolRegistry toolRegistry,
        IDaprAgentContextAccessor accessor,
        IServiceProvider? serviceProvider = null,
        IToolApprovalHandler? approvalHandler = null)
    {
        serviceProvider ??= new EmptyServiceProvider();
        var agentRegistry = new AgentRegistry(serviceProvider, []);
        agentRegistry.AddFactory(_ => new TestAIAgent(AgentName), null, AgentName, serviceProvider);

        return new ExecuteToolActivity(
            toolRegistry,
            agentRegistry,
            accessor,
            approvalHandler ?? new StubToolApprovalHandler(ToolApprovalDecision.Approve()),
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
            new StubToolApprovalHandler(ToolApprovalDecision.Approve()),
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
            new StubToolApprovalHandler(ToolApprovalDecision.Approve()),
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

    private sealed class StubToolApprovalHandler(ToolApprovalDecision decision) : IToolApprovalHandler
    {
        public List<ToolApprovalRequest> Requests { get; } = [];

        public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(decision);
        }
    }
}
