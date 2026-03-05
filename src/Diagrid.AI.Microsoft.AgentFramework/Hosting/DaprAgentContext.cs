// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Dapr.Workflow;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Ambient context available during agent tool invocation exposing a <see cref="DaprWorkflowClient"/>
/// to schedule/access workflows.
/// </summary>
/// <param name="workflowClient">The Dapr Workflow client.</param>
/// <param name="currentWorkflowInstanceId">The current workflow instance ID in whose activity this agent/tool is executing.</param>
public sealed class DaprAgentContext(DaprWorkflowClient workflowClient, string? currentWorkflowInstanceId = null)
{
    /// <summary>
    /// The current workflow instance ID in whose activity this agent/tool is executing.
    /// </summary>
    /// <remarks>
    /// Useful for correlation and raising external events back to the same instance.
    /// </remarks>
    public string? CurrentWorkflowInstanceId { get; } = currentWorkflowInstanceId;
    
    /// <summary>
    /// Schedules a new Dapr workflow.
    /// </summary>
    /// <param name="workflowName">The name of the workflow to schedule.</param>
    /// <param name="input">The input payload of the workflow.</param>
    /// <param name="instanceId">The optional instance ID; if omitted, a new GUID string is generated.</param>
    /// <typeparam name="TInput">The input type of the workflow.</typeparam>
    /// <returns>The instance ID of the scheduled workflow.</returns>
    public string ScheduleNewWorkflow<TInput>(string workflowName, TInput input, string? instanceId = null)
    {
        var id = instanceId ?? Guid.NewGuid().ToString("N");
        workflowClient.ScheduleNewWorkflowAsync(workflowName, id, input);
        return id;
    }
}
