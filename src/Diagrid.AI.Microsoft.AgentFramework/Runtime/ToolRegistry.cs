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

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Persistent in-process registry that stores <see cref="AIFunction"/> instances by agent name
/// and function name. Unlike <c>PendingFunctionRegistry</c>, entries persist for the lifetime
/// of the process, allowing tool activities to resolve functions after a crash recovery.
/// </summary>
internal sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, AIFunction> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a tool function for the specified agent.
    /// </summary>
    public void Register(string agentName, AIFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(function);
        var key = MakeKey(agentName, function.Name);
        _tools[key] = function;
    }

    /// <summary>
    /// Returns the <see cref="AIFunction"/> registered for <paramref name="agentName"/> and
    /// <paramref name="functionName"/>, or <c>null</c> if not found.
    /// </summary>
    public AIFunction? Get(string agentName, string functionName) =>
        _tools.GetValueOrDefault(MakeKey(agentName, functionName));

    private static string MakeKey(string agentName, string functionName) =>
        $"{agentName}:{functionName}";
}
