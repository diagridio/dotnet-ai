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
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Minimal workflow wrapper used when scheduling an agent invocation from outside a workflow (e.g., via
/// <see cref="DaprWorkflowClient"/>.
/// </summary>
public sealed class AgentRunWorkflow : Workflow<DaprAgentInvocation, AgentRunResponse>
{
    /// <inheritdoc />
    public override async Task<AgentRunResponse> RunAsync(WorkflowContext context, DaprAgentInvocation input) =>
        await context.CallActivityAsync<AgentRunResponse>(nameof(InvokeAgentActivity), input);
}
