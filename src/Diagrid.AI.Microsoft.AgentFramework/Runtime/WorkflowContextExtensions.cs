// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Convenience helpers for invoking agents from within Dapr Workflows.
/// </summary>
public static partial class WorkflowContextExtensions
{
    /// <summary>
    /// Gets the agent reference for a previously-registered agent by name.
    /// </summary>
    /// <param name="_"></param>
    /// <param name="agentName">The name of the agent used during registration.</param>
    /// <returns>The reference to the <see cref="AIAgent"/>.</returns>
    public static IDaprAIAgent GetAgent(this WorkflowContext _, string agentName) => new DaprAIAgent(agentName);

    /// <summary>
    /// Gets the agent reference for a previously-registered agent by name and chat client key.
    /// </summary>
    /// <param name="_"></param>
    /// <param name="agentName">The name of the agent used during registration.</param>
    /// <param name="chatClientKey">The chat client key used during registration.</param>
    /// <returns>The reference to the <see cref="AIAgent"/>.</returns>
    public static IDaprAIAgent GetAgent(this WorkflowContext _, string agentName, string? chatClientKey) =>
        new DaprAIAgent(agentName, chatClientKey);

    /// <summary>
    /// Invokes an agent insides an activity and returns the raw <see cref="AgentRunResponse"/>.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The <see cref="AIAgent"/> reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <returns>The raw agent response.</returns>
    public static Task<AgentRunResponse> RunAgentAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null) =>
        context.CallActivityAsync<AgentRunResponse>(nameof(InvokeAgentActivity),
            new DaprAgentInvocation(agent.Name, message, thread, options)
            {
                ChatClientKey = GetChatClientKey(agent),
            });

    /// <summary>
    /// Invokes an agent inside an activity and deserializes the response <see cref="AgentRunResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The <see cref="AIAgent"/> reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="thread">Optional thread to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="logger">Optional tool for logging.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    public static async Task<T?> RunAgentAndDeserializeAsync<T>(
        this WorkflowContext context,
        IDaprAIAgent agent,
        ILogger? logger = null,
        string? message = null,
        AgentThread? thread = null,
        AgentRunOptions? options = null)
    {
        logger ??= NullLogger.Instance;
        
        var messageLength = message?.Length ?? 0;
        LogAgentRunning(logger, agent.Name, messageLength);
        LogAgentRunningDebug(logger, agent.Name, message);
        var resp = await context.RunAgentAsync(agent, message, thread, options);
        var responseLength = resp.Text?.Length ?? 0;
        LogAgentResponseInfo(logger, agent.Name, responseLength);
        LogAgentResponseDebug(logger, agent.Name, resp.Text);
        var text = resp.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            LogAgentEmptyResponse(logger);
            return default;
        }
        
        // Normalize to a JSON payload if the agent added preamble text or fences.
        text = MarkdownCodeFenceHelper.ExtractJsonPayload(text, logger);

        var ti = AgentJsonResolverAccessor.Resolver.GetTypeInfo<T>() ??
                 throw new InvalidOperationException(
                     $"No source-generated JsonTypeInfo registered for {typeof(T).FullName}.");
        
        var des = JsonSerializer.Deserialize(text, ti);
        return des;
    }

    [LoggerMessage(LogLevel.Information, "Running agent '{AgentName}' with message length {MessageLength}")]
    private static partial void LogAgentRunning(ILogger logger, string agentName, int messageLength);

    [LoggerMessage(LogLevel.Debug, "Running agent '{AgentName}' with message '{Message}'")]
    private static partial void LogAgentRunningDebug(ILogger logger, string agentName, string? message);

    [LoggerMessage(LogLevel.Information, "Agent '{AgentName}' responded with text length {ResponseLength}")]
    private static partial void LogAgentResponseInfo(ILogger logger, string agentName, int responseLength);

    [LoggerMessage(LogLevel.Debug, "Agent '{AgentName}' response text: '{ResponseText}'")]
    private static partial void LogAgentResponseDebug(ILogger logger, string agentName, string? responseText);

    [LoggerMessage(LogLevel.Warning, "The agent didn't respond with a text value")]
    private static partial void LogAgentEmptyResponse(ILogger logger);

    private static string? GetChatClientKey(IDaprAIAgent agent) =>
        agent is DaprAIAgent daprAgent ? daprAgent.ChatClientKey : null;
}
