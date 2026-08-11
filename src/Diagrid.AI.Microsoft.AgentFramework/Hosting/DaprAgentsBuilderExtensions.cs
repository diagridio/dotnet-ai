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
    /// Registers a keyed <see cref="DaprChatClient"/> and a named agent that uses it, with a set of
    /// skills. Skills are portable packages of instructions, resources, and scripts that give the
    /// agent domain-specific expertise at runtime. They are advertised in the agent's system prompt
    /// and loaded on demand through the <c>load_skill</c> / <c>read_skill_resource</c> tools, which
    /// run as durable workflow activities like any other tool.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The explicit agent name used for registration.</param>
    /// <param name="conversationComponentName">The name of the Dapr Conversation component.</param>
    /// <param name="instructions">The system instructions/prompt for the agent.</param>
    /// <param name="configureSkills">
    /// Configures the <see cref="AgentSkillsProviderBuilder"/> — for example
    /// <c>b =&gt; b.UseFileSkills(paths).UseSkill(inlineSkill)</c> to mix file-based
    /// (<c>SKILL.md</c>), inline (<c>AgentInlineSkill</c>), and class-based
    /// (<c>AgentClassSkill&lt;T&gt;</c>) skills.
    /// </param>
    /// <param name="description">The optional agent description.</param>
    /// <param name="tools">Additional tools available to the agent, beyond those contributed by skills.</param>
    /// <param name="configure">An optional <see cref="Action{T}"/> to configure the chat client options.</param>
    /// <param name="serviceLifetime">The <see cref="ServiceLifetime"/> of the chat client service.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithSkills(
        this IAgentsBuilder builder,
        string agentName,
        string conversationComponentName,
        string instructions,
        Action<AgentSkillsProviderBuilder> configureSkills,
        string? description = null,
        IReadOnlyList<AITool>? tools = null,
        Action<DaprChatClientOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationComponentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(configureSkills);

        var services = GetServices(builder);
        services.AddDaprChatClient(conversationComponentName, conversationComponentName, configure, serviceLifetime);

        return builder.WithAgent(new AgentFactoryRegistration(sp =>
        {
            var chatClient = sp.GetRequiredKeyedService<IChatClient>(conversationComponentName);
            var skillsProvider = BuildSkillsProvider(configureSkills);
            var contextProviders = new AIContextProvider[] { skillsProvider };

            var chatOptions = new ChatOptions { Instructions = instructions };
            if (tools is { Count: > 0 })
            {
                chatOptions.Tools = [.. tools];
            }

            var agentOptions = new ChatClientAgentOptions
            {
                Name = agentName,
                Description = description,
                ChatOptions = chatOptions,
                AIContextProviders = contextProviders,
            };

            var agent = chatClient.AsAIAgent(agentOptions);

            // Register for the per-activity workflow path — the raw chat client, instructions,
            // tools, and skills provider so CallLlmActivity can drive the context-provider
            // pipeline and ExecuteToolActivity can run the skill tools.
            RegisterAgentComponents(sp, agentName, chatClient, instructions, tools, contextProviders, agent);

            return agent;
        })
        {
            Name = agentName,
            ChatClientKey = conversationComponentName,
        });
    }

    /// <summary>
    /// Builds an <see cref="AgentSkillsProvider"/> from the caller's configuration. Read-only skill
    /// tools (<c>load_skill</c> / <c>read_skill_resource</c>) have their approval requirement
    /// disabled because the durable runtime executes tools directly and does not yet perform the
    /// human-approval round-trip; <c>run_skill_script</c> remains approval-gated and is not exposed
    /// until that gate is implemented.
    /// </summary>
    private static AgentSkillsProvider BuildSkillsProvider(Action<AgentSkillsProviderBuilder> configureSkills)
    {
        var skillsBuilder = new AgentSkillsProviderBuilder();

        // File-based skills require a script runner to be present even when no script is ever run.
        // Provide a disabled default (script execution is a later phase); the caller may override it
        // inside configureSkills. run_skill_script is not exposed today, so this never executes.
        skillsBuilder.UseFileScriptRunner(ScriptExecutionDisabledRunner);

        configureSkills(skillsBuilder);

        skillsBuilder.UseOptions(options =>
        {
            options.DisableLoadSkillApproval = true;
            options.DisableReadSkillResourceApproval = true;
        });

        return skillsBuilder.Build();
    }

    private static Task<object?> ScriptExecutionDisabledRunner(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        System.Text.Json.JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Skill script execution is not enabled. The run_skill_script tool and its human-approval " +
            "gate are not yet supported by the Dapr Workflow runtime.");

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
        chatClientRegistry.Register(agentName, WrapIfNeeded(rawChatClient), instructions, tools);

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
    /// <param name="sp">The service provider.</param>
    /// <param name="agentName">The agent name.</param>
    /// <param name="rawChatClient">The raw chat client to call the LLM with.</param>
    /// <param name="instructions">The agent's system instructions.</param>
    /// <param name="tools">The agent's tools, if any.</param>
    /// <param name="contextProviders">
    /// The <see cref="AIContextProvider"/> instances (e.g. an <c>AgentSkillsProvider</c>) attached
    /// to the agent, if any.
    /// </param>
    /// <param name="agent">
    /// The materialized agent, required only when <paramref name="contextProviders"/> is non-empty
    /// so their contributed tools can be discovered and registered.
    /// </param>
    private static void RegisterAgentComponents(
        IServiceProvider sp,
        string agentName,
        IChatClient rawChatClient,
        string? instructions,
        IReadOnlyList<AITool>? tools,
        IReadOnlyList<AIContextProvider>? contextProviders = null,
        AIAgent? agent = null)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            return;

        var chatClientRegistry = sp.GetRequiredService<ChatClientRegistry>();
        chatClientRegistry.Register(
            agentName,
            WrapIfNeeded(rawChatClient),
            instructions,
            tools as IList<AITool> ?? tools?.ToList(),
            contextProviders);

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

        if (contextProviders is { Count: > 0 } && agent is not null)
        {
            // Discover and register the invokable tools contributed by context providers (e.g. an
            // AgentSkillsProvider's load_skill / read_skill_resource functions) so that
            // ExecuteToolActivity can resolve them — including after a workflow replay in a fresh
            // process, where the agent factory (and hence this method) runs again.
            ContextProviderPipeline
                .InvokeAsync(agentName, agent, contextProviders, toolRegistry, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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
