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
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Registers what the per-activity workflow path needs from an agent (its raw chat client, instructions, tools, context
/// providers and options) in <see cref="ChatClientRegistry"/> and <see cref="ToolRegistry"/>. Kept out of the public
/// <see cref="DaprAgentsBuilderExtensions"/> so that class only holds the fluent API.
/// </summary>
internal static class AgentComponentRegistrar
{
    /// <summary>
    /// Extracts the <see cref="IChatClient"/>, instructions, and tools from a
    /// <see cref="ChatClientAgent"/> and registers them in the <see cref="ChatClientRegistry"/>
    /// and <see cref="ToolRegistry"/> so the workflow can call them as separate activities.
    /// Uses <see cref="DaprAgentsBuilder.GetAgentChatOptions"/> (backed by
    /// <c>[UnsafeAccessor]</c>) for AOT-safe access to the internal ChatOptions property.
    /// </summary>
    internal static void RegisterAgentComponents(
        IServiceProvider sp,
        AIAgent agent,
        IChatClient rawChatClient,
        string? registeredName = null)
    {
        var agentName = registeredName ?? agent.Name;
        if (string.IsNullOrWhiteSpace(agentName))
            return;

        string? instructions = null;
        IList<AITool>? tools = null;
        IReadOnlyList<AIContextProvider>? contextProviders = null;
        ChatOptions? baseOptions = null;

        if (agent is ChatClientAgent cca)
        {
            instructions = cca.Instructions;
            var agentOptions = DaprAgentsBuilder.GetAgentChatOptions(cca);
            tools = agentOptions?.Tools;
            baseOptions = ToBaseOptions(agentOptions);
            // Picks up AIContextProviders set via ChatClientAgentOptions (e.g. an MAF
            // AgentSkillsProvider) with no extra API needed for this (generic factory) path.
            contextProviders = cca.AIContextProviders is { Count: > 0 } ? cca.AIContextProviders : null;
        }

        var chatClientRegistry = sp.GetRequiredService<ChatClientRegistry>();
        chatClientRegistry.Register(agentName, WrapIfNeeded(rawChatClient), instructions, tools, contextProviders, baseOptions);

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
    /// The agent's own <see cref="ChatOptions"/> that <see cref="CallLlmActivity"/> starts every LLM
    /// call from (temperature, output limits, reasoning, ...), without what it supplies itself or
    /// cannot honour: the tools are merged per call, the instructions are sent as the system
    /// message, a server-side conversation id would make the provider see the conversation twice,
    /// and the workflow neither polls background responses nor resumes from a continuation token,
    /// so those are left to the run's own <see cref="AgentRunOptions"/>.
    /// </summary>
    private static ChatOptions? ToBaseOptions(ChatOptions? agentOptions)
    {
        if (agentOptions is null)
        {
            return null;
        }

        var baseOptions = agentOptions.Clone();
        baseOptions.Tools = null;
        baseOptions.Instructions = null;
        baseOptions.ConversationId = null;
#pragma warning disable MEAI001
        baseOptions.AllowBackgroundResponses = null;
        baseOptions.ContinuationToken = null;
#pragma warning restore MEAI001
        return baseOptions;
    }

    /// <summary>
    /// Registers components using explicitly provided values — no reflection or
    /// <c>[UnsafeAccessor]</c> needed since the caller already has the raw values.
    /// </summary>
    internal static void RegisterAgentComponents(
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

    internal static IServiceCollection GetServices(IAgentsBuilder builder) =>
        DaprAgentsBuilder.From(builder, "expose an IServiceCollection").Services;

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
