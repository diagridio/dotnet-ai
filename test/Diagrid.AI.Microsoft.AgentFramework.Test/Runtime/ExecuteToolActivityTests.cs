using System.Diagnostics;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ExecuteToolActivityTests
{
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
