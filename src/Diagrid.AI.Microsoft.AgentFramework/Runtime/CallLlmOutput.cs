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
/// Output payload from <see cref="CallLlmActivity"/>. Indicates whether the LLM returned
/// a final text response or requested tool calls.
/// </summary>
internal sealed record CallLlmOutput
{
    /// <summary>Whether this is a final response (no pending tool calls).</summary>
    public bool IsFinal { get; init; }

    /// <summary>Text content from the LLM (may be present even when tool calls are requested).</summary>
    public string? Text { get; init; }

    /// <summary>Error message if the LLM call failed.</summary>
    public string? Error { get; init; }

    /// <summary>Tool (function) calls requested by the LLM. Null or empty when <see cref="IsFinal"/> is true.</summary>
    public List<WorkflowFunctionCall>? FunctionCalls { get; init; }
}
