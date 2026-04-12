// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Dapr.Workflow;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Minimal workflow wrapper that executes a single tool invocation as its own Dapr Workflow activity.
/// Mirrors the pattern of <see cref="AgentRunWorkflow"/> for agent invocations.
/// </summary>
internal sealed class ToolRunWorkflow : Workflow<ToolInvocationInput, string>
{
    /// <inheritdoc />
    public override Task<string> RunAsync(WorkflowContext context, ToolInvocationInput input) =>
        context.CallActivityAsync<string>(nameof(InvokeToolActivity), input);
}
