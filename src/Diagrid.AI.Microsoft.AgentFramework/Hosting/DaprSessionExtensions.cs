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

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Extension methods for creating and working with Dapr Workflow-backed sessions.
/// </summary>
public static class DaprSessionExtensions
{
	/// <summary>
	/// Crates a new conversation session backed by a long-running Dapr Workflow. Pass the returned
	/// <see cref="AgentSession"/> to subsequent <c>RunAgentAsync</c> calls to maintain convesration continuity
	/// across turns.
	/// </summary>
	/// <param name="invoker">The agent invoker.</param>
	/// <param name="workflowClient">The Dapr Workflow client.</param>
	/// <param name="maxTurns">The optional maximum number of turns for this session.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>An <see cref="AgentSession"/> bound to the session workflow instance.</returns>
	public static async Task<AgentSession> CreateSessionAsync(
		this IDaprAgentInvoker invoker,
		DaprWorkflowClient workflowClient,
		uint? maxTurns = null,
		CancellationToken cancellationToken = default)
	{
		var instanceId = Guid.NewGuid().ToString("N");
		
		await workflowClient.ScheduleNewWorkflowAsync(
			name: nameof(SessionWorkflow),
			instanceId: instanceId,
			input: new SessionWorkflowInput { MaxTurns = maxTurns },
			null,
			cancellation: cancellationToken).ConfigureAwait(false);
		
		// Wait for the workflow to start and set initial custom status

		return CreateSessionFromInstanceId(instanceId);
	}

	/// <summary>
	/// Reattaches to an existing session workflow by instance ID. This is used to resume a conversation
	/// after serializing the session ID.
	/// </summary>
	/// <param name="invoker">The agent invoker.</param>
	/// <param name="sessionInstanceId">The workflow instance ID of the session.</param>
	/// <returns>An <see cref="AgentSession"/> bound to the existing session workflow.</returns>
	public static AgentSession AttachSession(this IDaprAgentInvoker invoker, string sessionInstanceId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionInstanceId);
		return CreateSessionFromInstanceId(sessionInstanceId);
	}

	/// <summary>
	/// Extracts the Dapr session workflow ID from an <see cref="AgentSession"/>, if present.
	/// </summary>
	/// <param name="session">The agent session to extract the value from.</param>
	/// <returns>The session ID, if present at all; otherwise null.</returns>
	public static string? GetSessionInstanceId(this AgentSession? session)
	{
		if (session?.StateBag.TryGetValue<string>(DaprSessionConstants.SessionInstanceIdKey, out var value) == true && value is { } sid)
			return sid;

		return null;
	}
	
	private static AgentSession CreateSessionFromInstanceId(string instanceId)
	{
		var session = new DaprSession();
		session.StateBag.SetValue(DaprSessionConstants.SessionInstanceIdKey, instanceId);
		return session;
	}
}