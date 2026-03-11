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
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Convenience helpers for working with <see cref="IDaprAgentInvoker"/>.
/// </summary>
public static class DaprAgentInvokerExtensions
{
    /// <summary>
    /// Gets a reference to a previously-registered agent by name and chat client key.
    /// </summary>
    /// <param name="invoker">The agent invoker.</param>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <param name="chatClientKey">The chat client key used during registration.</param>
    /// <returns>The reference to the agent.</returns>
    public static IDaprAIAgent GetAgent(this IDaprAgentInvoker invoker, string agentName, string? chatClientKey)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        return new DaprAIAgent(agentName, chatClientKey);
    }

    /// <summary>
    /// Invokes an agent inside a workflow activity and returns the raw <see cref="AgentRunResponse"/>.
    /// </summary>
    /// <param name="invoker">The agent invoker.</param>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="chatClientKey">Optional chat client key used during registration.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The raw agent response.</returns>
    public static Task<AgentRunResponse> RunAgentAsync(
        this IDaprAgentInvoker invoker,
        string agentName,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        string? chatClientKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        var agent = GetAgent(invoker, agentName, chatClientKey);
        return invoker.RunAgentAsync(agent, message, thread, options, cancellationToken);
    }

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="invoker">The agent invoker.</param>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <param name="logger">Optional tool for logging.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="chatClientKey">Optional chat client key used during registration.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    public static Task<T?> RunAgentAndDeserializeAsync<T>(
        this IDaprAgentInvoker invoker,
        string agentName,
        ILogger logger,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        string? chatClientKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        var agent = GetAgent(invoker, agentName, chatClientKey);
        return invoker.RunAgentAndDeserializeAsync<T>(
            agent,
            logger,
            message,
            thread,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="invoker">The agent invoker.</param>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="chatClientKey">Optional chat client key used during registration.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    public static Task<T?> RunAgentAndDeserializeAsync<T>(
        this IDaprAgentInvoker invoker,
        string agentName,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        string? chatClientKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        var agent = GetAgent(invoker, agentName, chatClientKey);
        return invoker.RunAgentAndDeserializeAsync<T>(
            agent,
            message,
            thread,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <typeparam name="TCategory">The logger category type.</typeparam>
    /// <param name="invoker">The agent invoker.</param>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="chatClientKey">Optional chat client key used during registration.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    public static Task<T?> RunAgentAndDeserializeAsync<T, TCategory>(
        this IDaprAgentInvoker invoker,
        string agentName,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        string? chatClientKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        var agent = GetAgent(invoker, agentName, chatClientKey);
        return invoker.RunAgentAndDeserializeAsync<T, TCategory>(
            agent,
            message,
            thread,
            options,
            cancellationToken);
    }
}
