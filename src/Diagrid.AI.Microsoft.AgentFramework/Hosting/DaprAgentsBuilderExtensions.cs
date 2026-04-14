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

using Dapr.AI.Microsoft.Extensions;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Convenience helpers for registering agents that use keyed chat clients.
/// </summary>
public static class DaprAgentsBuilderExtensions
{
    /// <summary>
    /// Registers an agent using an explicit registration record (including name/key).
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="registration">The agent factory registration.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(this IAgentsBuilder builder, AgentFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registration);

        if (builder is DaprAgentsBuilder daprBuilder)
        {
            return daprBuilder.WithAgentRegistration(registration);
        }

        throw new InvalidOperationException("The agents builder does not support explicit registrations.");
    }

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and an agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="factory">A factory that creates the <see cref="AIAgent"/> using the keyed chat client.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string conversationComponentName,
        Func<IChatClient, AIAgent> factory,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationComponentName);
        ArgumentNullException.ThrowIfNull(factory);

        var services = GetServices(builder);
        services.AddDaprChatClient(conversationComponentName, conversationComponentName, configure, serviceLifetime);

        return builder.WithAgent(conversationComponentName, sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(conversationComponentName);
            var agent = factory(chatClient);
            return agent.AsBuilder().UseToolActivityDispatch(sp).Build(sp);
        });
    }

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="factory">A factory that creates the <see cref="AIAgent"/> using the keyed chat client.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        Func<IChatClient, AIAgent> factory,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationComponentName);
        ArgumentNullException.ThrowIfNull(factory);

        var services = GetServices(builder);
        services.AddDaprChatClient(conversationComponentName, conversationComponentName, configure, serviceLifetime);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(conversationComponentName);
            var agent = factory(chatClient);
            return agent.AsBuilder().UseToolActivityDispatch(sp).Build(sp);
        })
        {
            Name = agentName,
            ChatClientKey = conversationComponentName,
        });
    }

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
        => WithAgent(
            builder,
            agentName,
            conversationComponentName,
            instructions,
            description: null,
            configure,
            serviceLifetime);

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="description">The optional agent description.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        string? description,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationComponentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);

        return builder.WithAgent(
            agentName,
            conversationComponentName,
            chat => chat.AsAIAgent(instructions: instructions, name: agentName, description: description),
            configure,
            serviceLifetime);
    }

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it, with a set of tools.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="tools">The tools available to the agent. Each invocation will be dispatched as a separate workflow activity.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        IReadOnlyList<AITool> tools,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
        => WithAgent(
            builder,
            agentName,
            conversationComponentName,
            instructions,
            description: null,
            tools,
            configure,
            serviceLifetime);

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it, with a set of tools.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="description">The optional agent description.</param>
    /// <param name="tools">The tools available to the agent. Each invocation will be dispatched as a separate workflow activity.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        string? description,
        IReadOnlyList<AITool> tools,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationComponentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(tools);

        return builder.WithAgent(
            agentName,
            conversationComponentName,
            chat => chat.AsAIAgent(instructions: instructions, name: agentName, description: description, tools: [.. tools]),
            configure,
            serviceLifetime);
    }

    private static IServiceCollection GetServices(IAgentsBuilder builder)
    {
        if (builder is DaprAgentsBuilder daprBuilder)
        {
            return daprBuilder.Services;
        }

        throw new InvalidOperationException("The agents builder does not expose an IServiceCollection.");
    }
}
