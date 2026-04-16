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

using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
        // Wrap the user's factory to extract IChatClient, instructions, and tools
        // for the per-activity workflow path.
        Func<IServiceProvider, AIAgent> wrappedFactory = sp =>
        {
            var agent = factory(sp);

            // If the agent was built from an IChatClient (ChatClientAgent), extract
            // the raw chat client and register it so CallLlmActivity can use it directly.
            if (agent is ChatClientAgent cca)
            {
                var rawChatClient = UnwrapFunctionInvoking(cca.ChatClient);
                DaprAgentsBuilderExtensions.RegisterAgentComponents(sp, agent, rawChatClient);
            }

            return agent;
        };

        return WithAgentRegistration(new AgentFactoryRegistration(wrappedFactory)
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

    /// <summary>
    /// Traverses the <see cref="IChatClient"/> pipeline and returns the first client
    /// that is NOT a <see cref="FunctionInvokingChatClient"/>.
    /// This gives us the raw client suitable for single-turn LLM calls.
    /// Uses <see cref="UnsafeAccessorAttribute"/> for AOT-safe access to the protected
    /// <see cref="DelegatingChatClient.InnerClient"/> property.
    /// </summary>
    internal static IChatClient UnwrapFunctionInvoking(IChatClient client)
    {
        while (client is FunctionInvokingChatClient fic)
        {
            var inner = GetInnerClient(fic);
            if (inner is null || ReferenceEquals(inner, client))
                break;
            client = inner;
        }

        return client;
    }

    /// <summary>
    /// Extracts the <see cref="ChatOptions"/> from a <see cref="ChatClientAgent"/> using
    /// <see cref="UnsafeAccessorAttribute"/> — AOT-safe, no runtime reflection.
    /// </summary>
    internal static ChatOptions? GetAgentChatOptions(ChatClientAgent agent) =>
        GetChatOptions(agent);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InnerClient")]
    private static extern IChatClient GetInnerClient(DelegatingChatClient client);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ChatOptions")]
    private static extern ChatOptions? GetChatOptions(ChatClientAgent agent);
}
