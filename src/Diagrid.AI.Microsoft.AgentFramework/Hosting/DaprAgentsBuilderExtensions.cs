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
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
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
            return factory(chatClient);
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
            return factory(chatClient);
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

        var services = GetServices(builder);
        services.AddDaprChatClient(conversationComponentName, conversationComponentName, configure, serviceLifetime);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(conversationComponentName);
            var agent = chatClient.AsAIAgent(instructions: instructions, name: agentName, description: description);

            // Register for per-activity workflow path — no reflection needed.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools: null);

            return agent;
        })
        {
            Name = agentName,
            ChatClientKey = conversationComponentName,
        });
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

        var services = GetServices(builder);
        services.AddDaprChatClient(conversationComponentName, conversationComponentName, configure, serviceLifetime);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(conversationComponentName);
            var agent = chatClient.AsAIAgent(
                instructions: instructions, name: agentName, description: description, tools: [.. tools]);

            // Register for per-activity workflow path — no reflection needed.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools);

            return agent;
        })
        {
            Name = agentName,
            ChatClientKey = conversationComponentName,
        });
    }

    /// <summary>
    /// Extracts the <see cref="IChatClient"/>, instructions, and tools from a
    /// <see cref="ChatClientAgent"/> and registers them in the <see cref="ChatClientRegistry"/>
    /// and <see cref="ToolRegistry"/> so the workflow can call them as separate activities.
    /// Uses <see cref="DaprAgentsBuilder.GetAgentChatOptions"/> (backed by
    /// <c>[UnsafeAccessor]</c>) for AOT-safe access to the internal ChatOptions property.
    /// </summary>
    internal static void RegisterAgentComponents(IServiceProvider sp, AIAgent agent, IChatClient rawChatClient)
    {
        var agentName = agent.Name;
        if (string.IsNullOrWhiteSpace(agentName))
            return;

        string? instructions = null;
        IList<AITool>? tools = null;

        if (agent is ChatClientAgent cca)
        {
            instructions = cca.Instructions;
            tools = DaprAgentsBuilder.GetAgentChatOptions(cca)?.Tools;
        }

        var chatClientRegistry = sp.GetRequiredService<ChatClientRegistry>();
        chatClientRegistry.Register(agentName, rawChatClient, instructions, tools);

        var toolRegistry = sp.GetRequiredService<ToolRegistry>();
        if (tools is { Count: > 0 })
        {
            foreach (var tool in tools)
            {
                if (tool is AIFunction fn)
                {
                    toolRegistry.Register(agentName, fn);
                }
            }
        }
    }

    /// <summary>
    /// Registers components using explicitly provided values — no reflection or
    /// <c>[UnsafeAccessor]</c> needed since the caller already has the raw values.
    /// </summary>
    private static void RegisterAgentComponents(
        IServiceProvider sp,
        string agentName,
        IChatClient rawChatClient,
        string? instructions,
        IReadOnlyList<AITool>? tools)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            return;

        var chatClientRegistry = sp.GetRequiredService<ChatClientRegistry>();
        chatClientRegistry.Register(agentName, rawChatClient, instructions, tools as IList<AITool> ?? tools?.ToList());

        if (tools is { Count: > 0 })
        {
            var toolRegistry = sp.GetRequiredService<ToolRegistry>();
            foreach (var tool in tools)
            {
                if (tool is AIFunction fn)
                {
                    toolRegistry.Register(agentName, fn);
                }
            }
        }
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
