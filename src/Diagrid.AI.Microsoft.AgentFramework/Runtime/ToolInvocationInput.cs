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
/// Payload passed from the tool-dispatch middleware to <see cref="ToolRunWorkflow"/> and
/// <see cref="InvokeToolActivity"/> for a single tool invocation.
/// </summary>
/// <param name="CallId">Key into <see cref="PendingFunctionRegistry"/> for the <c>AIFunction</c> to invoke.</param>
/// <param name="ArgumentsJson">JSON-serialized tool arguments (IDictionary&lt;string, object?&gt;).</param>
/// <param name="AgentName">Name of the owning agent — used for logging.</param>
/// <param name="FunctionName">Name of the tool function — used for logging.</param>
internal sealed record ToolInvocationInput(
    string CallId,
    string ArgumentsJson,
    string AgentName,
    string FunctionName);
