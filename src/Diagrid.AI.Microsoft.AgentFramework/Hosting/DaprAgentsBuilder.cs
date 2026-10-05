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
using Diagrid.AI.Microsoft.AgentFramework.Catalyst;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    public IAgentsBuilder WithCatalyst(DiagridCatalystOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Registry.ResourceName);

        Services.AddSingleton(Options.Create(options));
        Services.AddHostedService<CatalystAgentRegistryHostedService>();

        return this;
    }

    public IAgentsBuilder WithCatalyst()
    {
        var options = new DiagridCatalystOptions();
        Services.AddSingleton(Options.Create(options));
        Services.AddHostedService<CatalystAgentRegistryHostedService>();

        return this;
    }

    private IAgentsBuilder WithAgentCore(Func<IServiceProvider, AIAgent> factory, string? chatClientKey)
    {
        return WithAgentRegistration(new AgentFactoryRegistration(WrappedFactory)
        {
            ChatClientKey = chatClientKey,
        });

        // Wrap the user's factory to extract IChatClient, instructions, and tools
        // for the per-activity workflow path.
        AIAgent WrappedFactory(IServiceProvider sp)
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
        }
    }

    internal IAgentsBuilder WithAgentRegistration(AgentFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Services.AddSingleton(registration);
        return this;
    }

    /// <summary>
    /// Traverses the <see cref="IChatClient"/> pipeline and returns the client directly below the
    /// innermost <see cref="FunctionInvokingChatClient"/>, or <paramref name="client"/> itself when the
    /// pipeline has none. This gives us the raw client suitable for single-turn LLM calls, so tools
    /// run in their own workflow activity instead of inside the LLM call.
    /// <see cref="ChatClientAgent"/> may place decorators above the <see cref="FunctionInvokingChatClient"/>
    /// (e.g. its approval decorators), so the whole <see cref="DelegatingChatClient"/> chain is walked.
    /// Uses <see cref="UnsafeAccessorAttribute"/> for AOT-safe access to the protected
    /// <see cref="DelegatingChatClient.InnerClient"/> property.
    /// </summary>
    internal static IChatClient UnwrapFunctionInvoking(IChatClient client)
    {
        var result = client;
        var current = client;
        while (current is DelegatingChatClient delegating)
        {
            var inner = GetInnerClient(delegating);
            if (inner is null || ReferenceEquals(inner, current))
                break;

            if (current is FunctionInvokingChatClient)
                result = inner;

            current = inner;
        }

        return result;
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
