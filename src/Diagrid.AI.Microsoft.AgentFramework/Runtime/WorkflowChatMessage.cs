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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// JSON-serializable representation of a chat message that flows through the Dapr Workflow.
/// </summary>
internal sealed record WorkflowChatMessage
{
    public string Role { get; init; } = "";
    public string? Content { get; init; }
    public List<WorkflowFunctionCall>? FunctionCalls { get; init; }
    public List<WorkflowFunctionResult>? FunctionResults { get; init; }
}

/// <summary>
/// JSON-serializable representation of a function (tool) call from the LLM.
/// </summary>
internal sealed record WorkflowFunctionCall
{
    public string CallId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ArgumentsJson { get; init; } = "{}";
}

/// <summary>
/// JSON-serializable representation of a function (tool) result.
/// </summary>
internal sealed record WorkflowFunctionResult
{
    public string CallId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ResultJson { get; init; } = "null";
}
