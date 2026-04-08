// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Custom workflow that invokes EchoAgent via <see cref="WorkflowContextExtensions.GetAgent"/>
/// and <see cref="WorkflowContextExtensions.RunAgentAsync"/>, exercising the workflow-context path.
/// </summary>
internal sealed class EchoOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var agent    = context.GetAgent("EchoAgent");
        var response = await context.RunAgentAsync(agent, message: input);
        return response.Text ?? string.Empty;
    }
}

/// <summary>
/// Custom workflow that invokes CapitalAgent and deserializes its JSON response via
/// <see cref="WorkflowContextExtensions.RunAgentAndDeserializeAsync{T}"/>.
/// </summary>
internal sealed class CapitalOrchestrationWorkflow : Workflow<string, CapitalAnswer?>
{
    public override async Task<CapitalAnswer?> RunAsync(WorkflowContext context, string input)
    {
        var agent = context.GetAgent("CapitalAgent");
        return await context.RunAgentAndDeserializeAsync<CapitalAnswer>(agent, message: input);
    }
}

/// <summary>
/// Custom workflow that resolves a keyed agent (AlphaAgent / "chat-key-alpha") via
/// <see cref="WorkflowContextExtensions.GetAgent(WorkflowContext, string, string)"/>.
/// </summary>
internal sealed class KeyedOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var agent    = context.GetAgent("AlphaAgent", "chat-key-alpha");
        var response = await context.RunAgentAsync(agent, message: input);
        return response.Text ?? string.Empty;
    }
}
