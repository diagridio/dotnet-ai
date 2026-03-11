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
