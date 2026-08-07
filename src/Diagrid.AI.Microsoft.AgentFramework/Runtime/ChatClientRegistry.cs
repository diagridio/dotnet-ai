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

using System.Collections.Concurrent;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Stores per-agent raw <see cref="IChatClient"/> references, instructions, tool metadata, and
/// <see cref="AIContextProvider"/> instances (e.g. a skills provider). Used by <see cref="CallLlmActivity"/>
/// to call the LLM directly (without the agent's <c>FunctionInvokingChatClient</c> wrapper), and by
/// <see cref="ResolveAgentContextActivity"/>/<see cref="CompleteAgentContextActivity"/> to run the
/// context-provider pipeline for each agent run.
/// </summary>
internal sealed class ChatClientRegistry
{
    private readonly ConcurrentDictionary<string, AgentChatConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    // Context providers attached via WithContextProviders/WithSkills before the agent's factory has
    // run for the first time (agent registration is lazy). Register(...) drains and merges these in
    // for the target agent name the first time the chat-client config is written.
    private readonly ConcurrentDictionary<string, List<AIContextProvider>> _pendingContextProviders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a registry with no pre-attached context providers.
    /// </summary>
    public ChatClientRegistry()
    {
    }

    /// <summary>
    /// Creates a registry seeded with <see cref="ContextProviderRegistration"/> instances collected
    /// from DI (added via <c>WithContextProviders</c>/<c>WithSkills</c> at startup).
    /// </summary>
    public ChatClientRegistry(IEnumerable<ContextProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            AddPendingContextProviders(registration.AgentName, registration.ContextProviders);
        }
    }

    /// <summary>
    /// Registers the chat client configuration for the specified agent.
    /// </summary>
    public void Register(string agentName, IChatClient chatClient, string? instructions, IList<AITool>? tools) =>
        Register(agentName, chatClient, instructions, tools, contextProviders: null);

    /// <summary>
    /// Registers the chat client configuration for the specified agent, including any
    /// <see cref="AIContextProvider"/> instances (e.g. a skills provider) it should use.
    /// Any providers previously attached via <see cref="RegisterContextProviders"/> for the same
    /// agent name are merged in automatically.
    /// </summary>
    public void Register(
        string agentName,
        IChatClient chatClient,
        string? instructions,
        IList<AITool>? tools,
        IReadOnlyList<AIContextProvider>? contextProviders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(chatClient);

        var merged = MergeWithPendingContextProviders(agentName, contextProviders);
        _configs[agentName] = new AgentChatConfig(chatClient, instructions, tools, merged);
    }

    /// <summary>
    /// Returns the chat client configuration registered under <paramref name="agentName"/>, or <c>null</c>.
    /// </summary>
    public AgentChatConfig? Get(string agentName) =>
        _configs.GetValueOrDefault(agentName);

    /// <summary>
    /// Returns <c>true</c> if a configuration is registered for the specified agent.
    /// </summary>
    public bool Contains(string agentName) =>
        _configs.ContainsKey(agentName);

    /// <summary>
    /// Attaches <see cref="AIContextProvider"/> instances to an agent name ahead of the agent's
    /// factory materialization. Safe to call multiple times for the same agent name (providers
    /// accumulate). Called by <c>WithContextProviders</c>/<c>WithSkills</c> at DI setup, and by the
    /// <see cref="ContextProviderRegistration"/>-seeding constructor.
    /// </summary>
    public void RegisterContextProviders(string agentName, IReadOnlyList<AIContextProvider> contextProviders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(contextProviders);

        AddPendingContextProviders(agentName, contextProviders);
    }

    private void AddPendingContextProviders(string agentName, IReadOnlyList<AIContextProvider> contextProviders)
    {
        if (contextProviders.Count == 0)
        {
            return;
        }

        _pendingContextProviders.AddOrUpdate(
            agentName,
            _ => [..contextProviders],
            (_, existing) =>
            {
                existing.AddRange(contextProviders);
                return existing;
            });
    }

    private IReadOnlyList<AIContextProvider>? MergeWithPendingContextProviders(
        string agentName,
        IReadOnlyList<AIContextProvider>? explicitProviders)
    {
        // Pending providers are consumed (not just read) so re-registration doesn't duplicate them.
        var hasPending = _pendingContextProviders.TryRemove(agentName, out var pending);

        if (!hasPending || pending is { Count: 0 })
        {
            return explicitProviders is { Count: > 0 } ? explicitProviders : null;
        }

        if (explicitProviders is not { Count: > 0 })
        {
            return pending;
        }

        var merged = new List<AIContextProvider>(explicitProviders.Count + pending!.Count);
        merged.AddRange(explicitProviders);
        merged.AddRange(pending);
        return merged;
    }

    internal sealed record AgentChatConfig(
        IChatClient ChatClient,
        string? Instructions,
        IList<AITool>? Tools,
        IReadOnlyList<AIContextProvider>? ContextProviders = null);
}
