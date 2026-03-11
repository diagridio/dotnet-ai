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

using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Invokes registered agents by scheduling workflow-backed activities.
/// </summary>
public interface IDaprAgentInvoker
{
    /// <summary>
    /// Gets a reference to a previously-registered agent by name.
    /// </summary>
    /// <param name="agentName">The agent name used during registration.</param>
    /// <returns>The reference to the <see cref="AIAgent"/>.</returns>
    IDaprAIAgent GetAgent(string agentName);

    /// <summary>
    /// Invokes an agent inside a workflow activity and returns the raw <see cref="AgentRunResponse"/>.
    /// </summary>
    /// <param name="agent">The agent reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The raw agent response.</returns>
    Task<AgentRunResponse> RunAgentAsync(
        IDaprAIAgent agent,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="agent">The agent reference.</param>
    /// <param name="logger">Optional tool for logging.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    Task<T?> RunAgentAndDeserializeAsync<T>(
        IDaprAIAgent agent,
        ILogger logger,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="agent">The agent reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    Task<T?> RunAgentAndDeserializeAsync<T>(
        IDaprAIAgent agent,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes an agent inside a workflow activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <typeparam name="TCategory">The logger category type.</typeparam>
    /// <param name="agent">The agent reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="cancellationToken">Token to cancel the invocation.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    Task<T?> RunAgentAndDeserializeAsync<T, TCategory>(
        IDaprAIAgent agent,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);
}
