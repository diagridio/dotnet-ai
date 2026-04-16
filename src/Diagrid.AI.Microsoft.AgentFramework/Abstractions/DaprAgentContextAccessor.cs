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

using Diagrid.AI.Microsoft.AgentFramework.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Default implementation of <see cref="IDaprAgentContextAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is registered as a singleton, but is safe for concurrent use across
/// parallel tool activities because <see cref="AsyncLocal{T}"/> provides per-async-flow
/// isolation — each activity's async context sees its own value, so concurrent writes
/// from different <see cref="Runtime.ExecuteToolActivity"/> instances do not interfere.
/// </para>
/// <para>
/// This is the same pattern used by ASP.NET Core's <c>IHttpContextAccessor</c>.
/// </para>
/// </remarks>
internal sealed class DaprAgentContextAccessor : IDaprAgentContextAccessor
{
    private static readonly AsyncLocal<DaprAgentContext?> _current = new();

    /// <inheritdoc />
    public DaprAgentContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
