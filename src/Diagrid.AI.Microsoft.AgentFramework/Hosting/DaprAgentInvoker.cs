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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Invokes registered agents by scheduling workflow-backed activities.
/// </summary>
public sealed partial class DaprAgentInvoker(DaprWorkflowClient workflowClient, ILoggerFactory loggerFactory, ILogger<DaprAgentInvoker> logger) : IDaprAgentInvoker
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
            logger,
            GetChatClientKey(agent),
            message,
            session,
            options,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<T?> RunAgentAndDeserializeAsync<T>(
        IDaprAIAgent agent,
        ILogger innerLogger,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(innerLogger);
        var resp = await RunAgentAsyncCore(
            agent,
            innerLogger,
            GetChatClientKey(agent),
            message,
            session,
            options,
            cancellationToken).ConfigureAwait(false);

        var text = resp.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            LogAgentEmptyResponse();
            return default;
        }

        text = MarkdownCodeFenceHelper.ExtractJsonPayload(text, innerLogger);

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
        var innerLogger = loggerFactory.CreateLogger<TCategory>();
        return RunAgentAndDeserializeAsync<T>(agent, innerLogger, message, session, options, cancellationToken);
    }

    private async Task<AgentResponse> RunAgentAsyncCore(
        IDaprAIAgent agent,
        ILogger innerLogger,
        string? chatClientKey,
        string? message,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(innerLogger);

        var messageLength = message?.Length ?? 0;
        LogAgentRunning(agent.Name, chatClientKey, messageLength, session is not null, options is not null);
        LogAgentRunningDebug(agent.Name, message);
        
        // Check if this invocation is part of a session
        var sessionInstanceId = session.GetSessionInstanceId();

        AgentResponse response;
        string instanceId;

        if (sessionInstanceId is not null)
        {
            // Session-aware path: raise event on the session workflow
            response = await RunWithSessionAsync(agent, chatClientKey, message, sessionInstanceId,
                cancellationToken).ConfigureAwait(false);
            instanceId = sessionInstanceId;
        }
        else
        {
            // Stateless path: schedule a standalone workflow (existing behavior)
            (response, instanceId) =
                await RunStatelessAsync(agent, chatClientKey, message, session, options, cancellationToken)
                    .ConfigureAwait(false);
        }

        var responseLength = response.Text?.Length ?? 0;
        LogAgentResponseInfo(agent.Name, instanceId, responseLength);
        LogAgentResponseDebug(agent.Name, response.Text);
        return response;
    }

    private async Task<(AgentResponse Response, string InstanceId)> RunStatelessAsync(
        IDaprAIAgent agent,
        string? chatClientKey,
        string? message,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        var invocation = new DaprAgentInvocation(agent.Name, message, session, options)
        {
            ChatClientKey = chatClientKey
        };

        var instanceId = await workflowClient.ScheduleNewWorkflowAsync(
            name: nameof(AgentRunWorkflow),
            instanceId: null,
            input: invocation,
            startTime: null,
            cancellation: cancellationToken).ConfigureAwait(false);

        var state = await workflowClient.WaitForWorkflowCompletionAsync(
            instanceId, cancellation: cancellationToken).ConfigureAwait(false);

        if (state.RuntimeStatus != WorkflowRuntimeStatus.Completed)
        {
            var failure = state.FailureDetails;
            throw new InvalidOperationException(
                $"Agent workflow '{instanceId}' completed with status '{state.RuntimeStatus}'. {failure?.ErrorMessage}");
        }

        var result = state.ReadOutputAs<AgentRunResult>() ??
                     throw new InvalidOperationException(
                         $"Agent workflow '{instanceId}' completed without a response.");
        return (result.Response, instanceId);
    }

    private async Task<AgentResponse> RunWithSessionAsync(
        IDaprAIAgent agent,
        string? chatClientKey,
        string? message,
        string sessionInstanceId,
        CancellationToken cancellationToken)
    {
        var turnId = Guid.NewGuid().ToString("N");

        LogSessionTurnStart(agent.Name, sessionInstanceId, turnId);
        
        // Send the turn request as an external event to the session workflow
        await workflowClient.RaiseEventAsync(
            instanceId: sessionInstanceId,
            eventName: SessionWorkflow.TurnEventName,
            eventPayload: new SessionTurnRequest
            {
                AgentName = agent.Name,
                ChatClientKey = chatClientKey,
                Message = message,
                TurnId = turnId
            },
            cancellation: cancellationToken).ConfigureAwait(false);
        
        // Poll the session workflow's custom status until our turn completes
        return await PollForTurnCompletionAsync(sessionInstanceId, turnId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentResponse> PollForTurnCompletionAsync(
        string sessionInstanceId,
        string turnId,
        CancellationToken cancellationToken)
    {
        // Poll interval and timeout for waiting on session turn completion
        const int pollIntervalMs = 250;
        var timeout = TimeSpan.FromMinutes(10);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await workflowClient.GetWorkflowStateAsync(sessionInstanceId, cancellation: cancellationToken)
                .ConfigureAwait(false);

            if (state?.RuntimeStatus is WorkflowRuntimeStatus.Failed or WorkflowRuntimeStatus.Terminated)
            {
                throw new InvalidOperationException(
                    $"Session workflow '{sessionInstanceId}' is in status '{state.RuntimeStatus}'. {state.FailureDetails?.ErrorMessage}");
            }
            
            // Check if the custom status has our turn's response
            var turnStatus = state?.ReadCustomStatusAs<SessionTurnStatus>();
            if (turnStatus?.TurnId == turnId)
            {
                if (!turnStatus.Success)
                {
                    throw new InvalidOperationException($"Session turn '{turnId}' failed: {turnStatus.Error}");
                }

                return new AgentResponse(new ChatMessage(ChatRole.Assistant, turnStatus.ResponseText ?? string.Empty));
            }

            await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for session turn '{turnId}' to complete on session '{sessionInstanceId}'.");
    }

    private static string? GetChatClientKey(IDaprAIAgent agent) =>
        agent is DaprAIAgent daprAgent ? daprAgent.ChatClientKey : null;

    [LoggerMessage(LogLevel.Information,
        "Running agent '{AgentName}' (chat client key '{ChatClientKey}', message length {MessageLength}, has session {HasSession}, has options {HasOptions})")]
    private partial void LogAgentRunning(
        string agentName,
        string? chatClientKey,
        int messageLength,
        bool hasSession,
        bool hasOptions);

    [LoggerMessage(LogLevel.Debug, "Running agent '{AgentName}' with message '{Message}'")]
    private partial void LogAgentRunningDebug(string agentName, string? message);

    [LoggerMessage(LogLevel.Information,
        "Agent '{AgentName}' completed workflow '{InstanceId}' with response length {ResponseLength}")]
    private partial void LogAgentResponseInfo(string agentName, string instanceId, int responseLength);

    [LoggerMessage(LogLevel.Debug, "Agent '{AgentName}' response text: '{ResponseText}'")]
    private partial void LogAgentResponseDebug(string agentName, string? responseText);

    [LoggerMessage(LogLevel.Warning, "The agent didn't respond with a text value")]
    private partial void LogAgentEmptyResponse();
    
    [LoggerMessage(LogLevel.Information, "Starting session turn for agent '{AgentName}' on session '{SessionInstanceId}' (turn ID '{TurnId}')")]
    private partial void LogSessionTurnStart(string agentName, string sessionInstanceId, string turnId);
}
