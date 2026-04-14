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
/// In-process registry that holds <see cref="AIFunction"/> instances for the duration of a single
/// per-tool activity dispatch. An entry lives from when the middleware intercepts a tool call until
/// <see cref="InvokeToolActivity"/> completes and removes it.
/// </summary>
internal sealed class PendingFunctionRegistry
{
    private readonly ConcurrentDictionary<string, AIFunction> _pending = new();

    /// <summary>
    /// Registers a function and returns a unique call ID.
    /// </summary>
    public string Register(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var callId = Guid.NewGuid().ToString("N");
        _pending[callId] = function;
        return callId;
    }

    /// <summary>
    /// Returns the function registered under <paramref name="callId"/>, or <c>null</c> if not found.
    /// </summary>
    public AIFunction? Get(string callId) =>
        _pending.GetValueOrDefault(callId);

    /// <summary>
    /// Removes the function registered under <paramref name="callId"/>.
    /// </summary>
    public void Remove(string callId) => _pending.TryRemove(callId, out _);
}
