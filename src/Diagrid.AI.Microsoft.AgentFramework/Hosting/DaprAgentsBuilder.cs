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

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

internal sealed class DaprAgentsBuilder(IServiceCollection services) : IAgentsBuilder
{
    internal IServiceCollection Services { get; } = services;

    public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return WithAgentCore(factory, chatClientKey: null);
    }

    public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatClientKey);
        ArgumentNullException.ThrowIfNull(factory);

        return WithAgentCore(factory, chatClientKey);
    }

    private IAgentsBuilder WithAgentCore(Func<IServiceProvider, AIAgent> factory, string? chatClientKey)
    {
        return WithAgentRegistration(new AgentFactoryRegistration(factory)
        {
            ChatClientKey = chatClientKey,
        });
    }

    internal IAgentsBuilder WithAgentRegistration(AgentFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        Services.AddSingleton(registration);
        return this;
    }
}
