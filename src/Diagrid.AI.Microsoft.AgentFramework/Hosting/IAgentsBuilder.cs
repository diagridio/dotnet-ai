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

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Fluent builder returned form <see cref="DaprAgentsServiceCollectionExtensions.AddDaprAgents"/>. This is used
/// to chain the registration of one or more <see cref="AIAgent"/> instances.
/// </summary>
public interface IAgentsBuilder
{
    /// <summary>
    /// Registers an agent using a factory; the agent's <see cref="AIAgent.Name"/> is inferred and used as the 
    /// key.
    /// </summary>
    /// <param name="factory">A factory that creates the <see cref="AIAgent"/> using the app's
    /// <see cref="IServiceProvider"/>.</param>
    /// <returns></returns>
    IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory);

    /// <summary>
    /// Registers an agent using a factory with an associated chat client key.
    /// </summary>
    /// <param name="chatClientKey">The key used to resolve a keyed <see cref="IChatClient"/>.</param>
    /// <param name="factory">A factory that creates the <see cref="AIAgent"/> using the app's
    /// <see cref="IServiceProvider"/>.</param>
    /// <returns></returns>
    IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory);
}
