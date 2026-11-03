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
/// Finds an agent's chat client configuration for an activity. Agent registration is lazy, and an activity may run in
/// a fresh process after a replay, so the configuration may not be registered yet: then the agent's factory is run,
/// which registers it as a side effect.
/// </summary>
internal static class AgentChatConfigLookup
{
    /// <summary>
    /// The chat client configuration of <paramref name="agentName"/>, running the agent's factory first when it is not
    /// registered yet. <see langword="null"/> when the agent registers none (it is not a <c>ChatClientAgent</c>).
    /// </summary>
    /// <param name="chatClientRegistry">Where the configurations are registered.</param>
    /// <param name="agentRegistry">Resolves the agent, running its factory.</param>
    /// <param name="serviceProvider">The services the agent's factory is given.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="chatClientKey">The agent's chat client key, or <see langword="null"/>.</param>
    /// <returns>The configuration, or <see langword="null"/>.</returns>
    internal static ChatClientRegistry.AgentChatConfig? Find(
        ChatClientRegistry chatClientRegistry,
        AgentRegistry agentRegistry,
        IServiceProvider serviceProvider,
        string agentName,
        string? chatClientKey)
    {
        var config = chatClientRegistry.Get(agentName);
        if (config is null)
        {
            // Trigger lazy agent resolution — this runs the factory which populates
            // ChatClientRegistry (including any configured AIContextProviders) as a side effect
            // for ChatClientAgent types.
            agentRegistry.Get(agentName, chatClientKey, serviceProvider);
            config = chatClientRegistry.Get(agentName);
        }

        return config;
    }
}
