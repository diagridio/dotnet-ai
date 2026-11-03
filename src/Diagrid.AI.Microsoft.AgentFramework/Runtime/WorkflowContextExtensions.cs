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
using System.Text.Json.Serialization;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
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
    /// Invokes an agent as a child workflow and returns the raw <see cref="AgentResponse"/>.
    /// Each LLM call and each tool call within the agent run is a separate activity.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The <see cref="AIAgent"/> reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="session">Optional session to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <returns>The raw agent response.</returns>
    public static async Task<AgentResponse> RunAgentAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null)
    {
        var result = await context.CallChildWorkflowAsync<AgentRunResult>(nameof(AgentRunWorkflow),
            CreateInvocation(agent, message, options, SessionForInput(session), conversationId: session.GetConversationId()));
        return result.Response;
    }

    /// <summary>
    /// Invokes an agent as a child workflow and applies custom OpenTelemetry baggage inside
    /// the LLM and tool activities.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The <see cref="AIAgent"/> reference.</param>
    /// <param name="telemetryBaggage">Custom baggage values. Values with framework keys override the defaults inside activities.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="session">Optional session to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <returns>The raw agent response.</returns>
    public static async Task<AgentResponse> RunAgentWithTelemetryBaggageAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        IReadOnlyDictionary<string, string?> telemetryBaggage,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(telemetryBaggage);

        var result = await context.CallChildWorkflowAsync<AgentRunResult>(nameof(AgentRunWorkflow),
            CreateInvocation(agent, message, options, SessionForInput(session), conversationId: session.GetConversationId(),
                telemetryBaggage: telemetryBaggage));
        return result.Response;
    }

    /// <summary>
    /// Invokes an agent as a child workflow with prior conversation history, enabling multi-turn
    /// conversations within a workflow.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The agent reference.</param>
    /// <param name="message">The user/system message for this turn.</param>
    /// <param name="priorMessages">The prior conversation messages to include for context. Typically, these are
    /// accumulated across previous agent invocations in the same workflow.</param>
    /// <param name="options">Optional agent run options.</param>
    /// <param name="telemetryBaggage">Optional custom baggage values to apply inside workflow activities.</param>
    /// <returns>The agent run result including turn messages for accumulation.</returns>
    public static Task<AgentRunResult> RunAgentWithHistoryAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        string? message = null,
        List<WorkflowChatMessage>? priorMessages = null,
        AgentRunOptions? options = null,
        IReadOnlyDictionary<string, string?>? telemetryBaggage = null) =>
        context.CallChildWorkflowAsync<AgentRunResult>(
            nameof(AgentRunWorkflow),
            CreateInvocation(agent, message, options, priorMessages: priorMessages, telemetryBaggage: telemetryBaggage));

    /// <summary>
    /// Invokes an agent as a child workflow against a stored conversation. The agent sees every turn already
    /// committed to <paramref name="conversationId"/>, including its own earlier reasoning, and if the run
    /// succeeds its messages are appended as one turn. A failed run appends nothing, so retrying with the same
    /// id is safe.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The agent reference.</param>
    /// <param name="conversationId">
    /// The conversation id. Inside a workflow derive it deterministically, for example from
    /// <c>context.InstanceId</c> or <c>context.NewGuid()</c>, never from <c>Guid.NewGuid()</c>.
    /// </param>
    /// <param name="message">The user message for this turn.</param>
    /// <param name="options">Optional agent run options.</param>
    /// <param name="telemetryBaggage">Optional custom baggage values to apply inside workflow activities.</param>
    /// <returns>
    /// The agent run result. <see cref="AgentRunResult.TurnMessages"/> holds the turn that was appended, and
    /// <see cref="AgentRunResult.ConversationSequence"/> the conversation's length after it.
    /// </returns>
    /// <remarks>
    /// Requires <c>WithConversationStore(...)</c>. The history does not travel in workflow payloads: each LLM
    /// call loads it from the store, pinned to what the run's first call saw. Concurrent runs on one
    /// conversation are ordered by commit, and a run that did not see an earlier commit is still stored after
    /// it.
    /// </remarks>
    public static Task<AgentRunResult> RunAgentInConversationAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        string conversationId,
        string? message = null,
        AgentRunOptions? options = null,
        IReadOnlyDictionary<string, string?>? telemetryBaggage = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return context.CallChildWorkflowAsync<AgentRunResult>(
            nameof(AgentRunWorkflow),
            CreateInvocation(agent, message, options, conversationId: conversationId, telemetryBaggage: telemetryBaggage));
    }

    /// <summary>
    /// Deletes a stored conversation, as an activity of the current workflow.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="conversationId">The conversation id.</param>
    /// <returns><see langword="true"/> if the conversation existed; otherwise <see langword="false"/>.</returns>
    /// <remarks>Requires <c>WithConversationStore(...)</c>.</remarks>
    public static Task<bool> DeleteConversationAsync(this WorkflowContext context, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return context.CallActivityAsync<bool>(nameof(DeleteConversationActivity), conversationId);
    }

    /// <summary>
    /// Starts an agent run as a detached workflow and returns its instance ID without waiting for it.
    /// Each LLM call and each tool call within the agent run is a separate activity, as with
    /// <see cref="RunAgentAsync"/>.
    /// </summary>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The agent reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="options">Optional agent run options.</param>
    /// <param name="instanceId">The instance ID for the agent run, or <see langword="null"/> to have a deterministic one generated.</param>
    /// <param name="telemetryBaggage">Optional custom baggage values to apply inside workflow activities.</param>
    /// <returns>The instance ID of the scheduled agent run.</returns>
    /// <remarks>
    /// <para>
    /// Unlike a child workflow, the agent run is independent of the current workflow: the current
    /// workflow does not wait for it, a failed run does not fail it, and terminating it does not
    /// terminate the run. Use this for fire-and-forget work such as notifications or follow-ups that
    /// should not hold the caller open. The run's <see cref="AgentRunResult"/> is its workflow output,
    /// readable through <c>DaprWorkflowClient</c> with the returned instance ID.
    /// </para>
    /// <para>
    /// Requires a Dapr 1.19 or newer sidecar. An older sidecar rejects the action as unknown, treats
    /// that as recoverable and retries it indefinitely, so the calling workflow stalls rather than fails.
    /// </para>
    /// </remarks>
    public static Task<string> StartAgentRunDetachedAsync(
        this WorkflowContext context,
        IDaprAIAgent agent,
        string? message = null,
        AgentRunOptions? options = null,
        string? instanceId = null,
        IReadOnlyDictionary<string, string?>? telemetryBaggage = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agent);

        // No session parameter: a session's turns are ordered by its SessionWorkflow, and a run nobody
        // awaits could not keep that order.
        return context.ScheduleNewDetachedWorkflowAsync(
            nameof(AgentRunWorkflow),
            instanceId,
            CreateInvocation(agent, message, options, telemetryBaggage: telemetryBaggage));
    }

    /// <summary>
    /// Invokes an agent inside an activity and deserializes the response <see cref="AgentResponse.Text"/> to
    /// <typeparamref name="T"/> using a source-generated <see cref="JsonSerializerContext"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize.</typeparam>
    /// <param name="context">The current workflow context.</param>
    /// <param name="agent">The <see cref="AIAgent"/> reference.</param>
    /// <param name="message">Optional user/system message.</param>
    /// <param name="session">Optional session to use for conversation state.</param>
    /// <param name="options">Optional <see cref="AgentRunOptions"/> for invocation.</param>
    /// <param name="logger">Optional tool for logging.</param>
    /// <returns>The typed result, or <c>null</c> when no text was returned.</returns>
    public static async Task<T?> RunAgentAndDeserializeAsync<T>(
        this WorkflowContext context,
        IDaprAIAgent agent,
        ILogger? logger = null,
        string? message = null,
        AgentSession? session = null,
        AgentRunOptions? options = null)
    {
        logger ??= NullLogger.Instance;
        
        var messageLength = message?.Length ?? 0;
        LogAgentRunning(logger, agent.Name, messageLength);
        LogAgentRunningDebug(logger, agent.Name, message);
        var resp = await context.RunAgentAsync(agent, message, session, options);
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

    /// <summary>
    /// The session to put in the run's input. A session from <c>AttachConversation</c> carries only its id,
    /// which travels as <see cref="DaprAgentInvocation.ConversationId"/>.
    /// </summary>
    private static AgentSession? SessionForInput(AgentSession? session) =>
        session.GetConversationId() is null ? session : null;

    /// <summary>
    /// The input of an <see cref="AgentRunWorkflow"/> started for <paramref name="agent"/>. Each caller passes only what
    /// it carries; the rest stays <see langword="null"/>, as the property initializers it replaces left it. The baggage is
    /// copied, and no baggage, or an empty one, leaves <see cref="DaprAgentInvocation.TelemetryBaggage"/> unset.
    /// </summary>
    internal static DaprAgentInvocation CreateInvocation(
        IDaprAIAgent agent,
        string? message,
        AgentRunOptions? options,
        AgentSession? session = null,
        string? conversationId = null,
        List<WorkflowChatMessage>? priorMessages = null,
        IReadOnlyDictionary<string, string?>? telemetryBaggage = null) =>
        new(agent.Name, message, session, options)
        {
            ChatClientKey = GetChatClientKey(agent),
            ConversationId = conversationId,
            PriorMessages = priorMessages,
            TelemetryBaggage = AgentTelemetryBaggage.Copy(telemetryBaggage)
        };

    private static string? GetChatClientKey(IDaprAIAgent agent) =>
        agent is DaprAIAgent daprAgent ? daprAgent.ChatClientKey : null;
}
