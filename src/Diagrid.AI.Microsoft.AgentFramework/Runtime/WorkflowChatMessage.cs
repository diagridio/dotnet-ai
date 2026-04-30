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
public sealed record WorkflowChatMessage
{
    /// <summary>
    /// The chat role (e.g. <c>user</c>, <c>assistant</c>, <c>tool</c>).
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Text content of the message, or <see langword="null"/> when the message contains only tool calls or results.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls requested by the LLM in this message, or <see langword="null"/> if none.
    /// </summary>
    public List<WorkflowFunctionCall>? FunctionCalls { get; init; }

    /// <summary>
    /// Results returned to the LLM for previously requested tool calls, or <see langword="null"/> if none.
    /// </summary>
    public List<WorkflowFunctionResult>? FunctionResults { get; init; }
}

/// <summary>
/// JSON-serializable representation of a function (tool) call from the LLM.
/// </summary>
public sealed record WorkflowFunctionCall
{
    /// <summary>
    /// Unique identifier for this call, used to correlate it with the corresponding <see cref="WorkflowFunctionResult"/>.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// Name of the tool/function to invoke.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// JSON-encoded arguments to pass to the function.
    /// </summary>
    public string ArgumentsJson { get; init; } = "{}";
}

/// <summary>
/// JSON-serializable representation of a function (tool) result.
/// </summary>
public sealed record WorkflowFunctionResult
{
    /// <summary>
    /// Identifier matching the <see cref="WorkflowFunctionCall.CallId"/> this result corresponds to.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// Name of the tool/function that produced this result.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// JSON-encoded return value from the function, or <see langword="null"/> if the function returned nothing.
    /// </summary>
    public string? ResultJson { get; init; }
}
