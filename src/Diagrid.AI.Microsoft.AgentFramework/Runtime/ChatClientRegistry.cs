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
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Stores per-agent raw <see cref="IChatClient"/> references, instructions, and tool metadata.
/// Used by <see cref="CallLlmActivity"/> to call the LLM directly (without the agent's
/// <c>FunctionInvokingChatClient</c> wrapper).
/// </summary>
internal sealed class ChatClientRegistry
{
    private readonly ConcurrentDictionary<string, AgentChatConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers the chat client configuration for the specified agent.
    /// </summary>
    public void Register(string agentName, IChatClient chatClient, string? instructions, IList<AITool>? tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(chatClient);
        _configs[agentName] = new AgentChatConfig(chatClient, instructions, tools);
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

    internal sealed record AgentChatConfig(IChatClient ChatClient, string? Instructions, IList<AITool>? Tools);
}
