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

using System.Diagnostics.CodeAnalysis;
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

    // =========================================================================
    // AIContextProviders / Skills
    //
    // A key aspect of Skills support (https://github.com/diagridio/dotnet-ai/issues/34) is making
    // sure that AIContextProviders registered at startup are passed through to the context-provider
    // pipeline that ResolveAgentContextActivity/CompleteAgentContextActivity run once per agent run.
    // WithContextProviders is the general-purpose entry point (stable — AIContextProvider itself
    // carries no MAF experimental marker); WithSkills is sugar over an MAF AgentSkillsProvider,
    // which IS marked [Experimental("MAAI001")] upstream, so these overloads propagate that marker.
    // =========================================================================

    /// <summary>
    /// Attaches one or more <see cref="AIContextProvider"/> instances (e.g. an MAF
    /// <c>AgentSkillsProvider</c>, a memory provider, etc.) to the named agent. Their contributed
    /// instructions/messages/tools are resolved once per run and made available throughout that run
    /// — see <c>ResolveAgentContextActivity</c>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent to attach the providers to.</param>
    /// <param name="contextProviders">The context providers to attach.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// Can be called before or after <c>WithAgent(...)</c> for the same agent name — the providers
    /// are attached lazily, when the agent is first materialized.
    /// </remarks>
    public static IAgentsBuilder WithContextProviders(
        this IAgentsBuilder builder,
        string agentName,
        IReadOnlyList<AIContextProvider> contextProviders)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(contextProviders);

        var services = GetServices(builder);
        services.AddSingleton(new ContextProviderRegistration(agentName, contextProviders));

        return builder;
    }

    /// <summary>
    /// Attaches one or more <see cref="AIContextProvider"/> instances to the named agent.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent to attach the providers to.</param>
    /// <param name="contextProviders">The context providers to attach.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithContextProviders(
        this IAgentsBuilder builder,
        string agentName,
        params AIContextProvider[] contextProviders) =>
        WithContextProviders(builder, agentName, (IReadOnlyList<AIContextProvider>)contextProviders);

    /// <summary>
    /// Attaches an MAF skills provider built from the given <see cref="AgentSkill"/> instances to the
    /// named agent — covers file-based (<c>AgentFileSkill</c>, via <c>AgentSkillsProvider(skillPath)</c>
    /// on the other overload), inline (<see cref="AgentInlineSkill"/>), and class-based
    /// (<c>AgentClassSkill&lt;T&gt;</c>) skill sources, since they all derive from <see cref="AgentSkill"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent to attach the skills to.</param>
    /// <param name="skills">The skills to make available to the agent.</param>
    /// <returns>The agents builder.</returns>
    [Experimental("MAAI001")]
    public static IAgentsBuilder WithSkills(
        this IAgentsBuilder builder,
        string agentName,
        params AgentSkill[] skills)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(skills);

        return builder.WithContextProviders(agentName, new AgentSkillsProvider(skills));
    }

    /// <summary>
    /// Attaches an MAF skills provider to the named agent, built via <see cref="AgentSkillsProviderBuilder"/>.
    /// Use this overload to mix file-based, inline, and class-based skill sources, configure script
    /// approval (<c>UseScriptApproval()</c>), a custom file script runner, filters, or prompt template.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent to attach the skills to.</param>
    /// <param name="configureSkills">Callback to configure the <see cref="AgentSkillsProviderBuilder"/>.</param>
    /// <returns>The agents builder.</returns>
    [Experimental("MAAI001")]
    public static IAgentsBuilder WithSkills(
        this IAgentsBuilder builder,
        string agentName,
        Action<AgentSkillsProviderBuilder> configureSkills)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(configureSkills);

        var skillsBuilder = new AgentSkillsProviderBuilder();
        configureSkills(skillsBuilder);

        return builder.WithContextProviders(agentName, skillsBuilder.Build());
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
    /// Registers a named agent using the default registered <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string instructions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var agent = chatClient.AsAIAgent(instructions: instructions, name: agentName);

            // Register for per-activity workflow path — no reflection needed.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools: null);

            return agent;
        })
        {
            Name = agentName,
        });
    }
    
    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="chatClientKey">The key of the registered <see cref="IChatClient"/> to register with the agent.</param>
    /// <param name="description">The optional agent description.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string instructions,
        string chatClientKey,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatClientKey);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(chatClientKey);
            var agent = chatClient.AsAIAgent(instructions: instructions, name: agentName, description: description);

            // Register for per-activity workflow path — no reflection needed.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools: null);

            return agent;
        })
        {
            Name = agentName,
        });
    }

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="chatClient">The <see cref="IChatClient"/> to register with the agent.</param>
    /// <param name="description">The optional agent description.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string instructions,
        IChatClient chatClient,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var agent = chatClient.AsAIAgent(instructions: instructions, name: agentName, description: description);

            // Register for per-activity workflow path — no reflection needed.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools: null);

            return agent;
        })
        {
            Name = agentName,
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
        => WithAgent(builder, agentName, conversationComponentName, instructions, description: null, configure, serviceLifetime);

    /// <summary>
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="description">The agent description, or <c>null</c> for no description.</param>
    /// <param name="configure">An <see cref="Action{T}"/> to configure the chat client options, or <c>null</c> for defaults.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithAgent(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        string? description,
        Action<DaprChatClientOptions>? configure,
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
        IReadOnlyList<AIContextProvider>? contextProviders = null;

        if (agent is ChatClientAgent cca)
        {
            instructions = cca.Instructions;
            tools = DaprAgentsBuilder.GetAgentChatOptions(cca)?.Tools;
            // Picks up AIContextProviders set via ChatClientAgentOptions (e.g. an MAF
            // AgentSkillsProvider) with no extra API needed for this (generic factory) path.
            contextProviders = cca.AIContextProviders is { Count: > 0 } ? cca.AIContextProviders : null;
        }

        var chatClientRegistry = sp.GetRequiredService<ChatClientRegistry>();
        chatClientRegistry.Register(agentName, WrapIfNeeded(rawChatClient), instructions, tools, contextProviders);

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
        chatClientRegistry.Register(agentName, WrapIfNeeded(rawChatClient), instructions, tools as IList<AITool> ?? tools?.ToList());

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

    /// <summary>
    /// Wraps <paramref name="client"/> with <see cref="ToolResultCompatibilityChatClient"/>
    /// when the underlying implementation is <c>DaprChatClient</c>, which does not natively
    /// support <see cref="FunctionResultContent"/> in multi-turn tool conversations
    /// (Dapr.AI.Microsoft.Extensions ≤ 1.17.x).
    /// </summary>
    private static IChatClient WrapIfNeeded(IChatClient client) =>
        client.GetType().Name == "DaprChatClient"
            ? new ToolResultCompatibilityChatClient(client)
            : client;
}
