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



using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Helper record to allow DI to collect agent factories.
/// </summary>
/// <param name="Factory"></param>
public sealed record AgentFactoryRegistration(Func<IServiceProvider, AIAgent> Factory)
{
    /// <summary>
    /// Optional explicit agent name to avoid eager factory invocation.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional key used to disambiguate agents by chat client.
    /// </summary>
    public string? ChatClientKey { get; init; }
}
