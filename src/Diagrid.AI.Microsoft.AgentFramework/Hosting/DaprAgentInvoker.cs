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

using System.Text.Json;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Invokes registered agents by scheduling workflow-backed activities.
/// </summary>
public sealed partial class DaprAgentInvoker(DaprWorkflowClient workflowClient, ILoggerFactory loggerFactory) : IDaprAgentInvoker
{
    /// <inheritdoc />
    public IDaprAIAgent GetAgent(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        return new DaprAIAgent(agentName);
    }

    /// <inheritdoc />
    public async Task<AgentResponse> RunAgentAsync(
        IDaprAIAgent agent,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await RunAgentAsyncCore(
            agent,
            loggerFactory.CreateLogger<DaprAgentInvoker>(),
            GetChatClientKey(agent),
            message,
            session,
            options,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<T?> RunAgentAndDeserializeAsync<T>(
        IDaprAIAgent agent,
        ILogger logger,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);
        var resp = await RunAgentAsyncCore(
            agent,
            logger,
            GetChatClientKey(agent),
            message,
            session,
            options,
            cancellationToken).ConfigureAwait(false);

        var text = resp?.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            LogAgentEmptyResponse(logger);
            return default;
        }

        text = MarkdownCodeFenceHelper.ExtractJsonPayload(text, logger);

        var typeInfo = AgentJsonResolverAccessor.Resolver.GetTypeInfo<T>() ??
            throw new InvalidOperationException(
                $"No source-generated JsonTypeInfo registered for {typeof(T).FullName}.");

        return JsonSerializer.Deserialize(text, typeInfo);
    }

    /// <inheritdoc />
    public Task<T?> RunAgentAndDeserializeAsync<T>(
        IDaprAIAgent agent,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunAgentAndDeserializeAsync<T>(agent, loggerFactory.CreateLogger<DaprAgentInvoker>(), message, session, options, cancellationToken);

    /// <inheritdoc />
    public Task<T?> RunAgentAndDeserializeAsync<T, TCategory>(
        IDaprAIAgent agent,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var logger = loggerFactory.CreateLogger<TCategory>();
        return RunAgentAndDeserializeAsync<T>(agent, logger, message, session, options, cancellationToken);
    }

    private async Task<AgentResponse> RunAgentAsyncCore(
        IDaprAIAgent agent,
        ILogger logger,
        string? chatClientKey,
        string? message,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);

        var messageLength = message?.Length ?? 0;
        LogAgentRunning(logger, agent.Name, chatClientKey, messageLength, session is not null, options is not null);
        LogAgentRunningDebug(logger, agent.Name, message);

        var invocation = new DaprAgentInvocation(agent.Name, message, session, options)
        {
            ChatClientKey = chatClientKey,
        };
        var instanceId = await workflowClient.ScheduleNewWorkflowAsync(
            name: nameof(AgentRunWorkflow),
            instanceId: null,
            input: invocation,
            startTime: null,
            cancellation: cancellationToken).ConfigureAwait(false);

        var state = await workflowClient.WaitForWorkflowCompletionAsync(
            instanceId,
            cancellation: cancellationToken).ConfigureAwait(false);

        if (state.RuntimeStatus != WorkflowRuntimeStatus.Completed)
        {
            var failure = state.FailureDetails;
            throw new InvalidOperationException(
                $"Agent workflow '{instanceId}' completed with status '{state.RuntimeStatus}'. " +
                $"{failure?.ErrorMessage}");
        }

        var response = state.ReadOutputAs<AgentResponse>() ??
                       throw new InvalidOperationException($"Agent workflow '{instanceId}' completed without a response.");
        var responseLength = response.Text?.Length ?? 0;
        LogAgentResponseInfo(logger, agent.Name, instanceId, responseLength);
        LogAgentResponseDebug(logger, agent.Name, response.Text);
        return response;
    }

    private static string? GetChatClientKey(IDaprAIAgent agent) =>
        agent is DaprAIAgent daprAgent ? daprAgent.ChatClientKey : null;

    [LoggerMessage(LogLevel.Information,
        "Running agent '{AgentName}' (chat client key '{ChatClientKey}', message length {MessageLength}, has session {HasSession}, has options {HasOptions})")]
    private static partial void LogAgentRunning(
        ILogger logger,
        string agentName,
        string? chatClientKey,
        int messageLength,
        bool hasSession,
        bool hasOptions);

    [LoggerMessage(LogLevel.Debug, "Running agent '{AgentName}' with message '{Message}'")]
    private static partial void LogAgentRunningDebug(ILogger logger, string agentName, string? message);

    [LoggerMessage(LogLevel.Information,
        "Agent '{AgentName}' completed workflow '{InstanceId}' with response length {ResponseLength}")]
    private static partial void LogAgentResponseInfo(ILogger logger, string agentName, string instanceId, int responseLength);

    [LoggerMessage(LogLevel.Debug, "Agent '{AgentName}' response text: '{ResponseText}'")]
    private static partial void LogAgentResponseDebug(ILogger logger, string agentName, string? responseText);

    [LoggerMessage(LogLevel.Warning, "The agent didn't respond with a text value")]
    private static partial void LogAgentEmptyResponse(ILogger logger);
}
