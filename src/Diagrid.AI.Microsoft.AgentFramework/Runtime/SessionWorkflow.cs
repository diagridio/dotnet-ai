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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// A long-running workflow that reprsents a conversational session. Each turn is received as an external event,
/// dispatched as a child <see cref="AgentRunWorkflow"/>, and the conversation log is accumulated as workflow-internal
/// state.
/// </summary>
public sealed class SessionWorkflow : Workflow<SessionWorkflowInput, string>
{
	/// <summary>
	/// Well-known event name used to send a turn request to the session.
	/// </summary>
	internal const string TurnEventName = "dapr-session-turn";
	
	/// <inheritdocs />
	public override async Task<string> RunAsync(WorkflowContext context, SessionWorkflowInput input)
	{
		// Maximum turns before the session workflow completes to prevent unbounded workflow history growth.
		var maxTurns = input.MaxTurns;
		
		// Accumulated conversation log across all turns
		// This list is persisted as part of the workflow state
		List<WorkflowChatMessage> conversationLog = [];
		var turnCount = 0;
		
		// Set initial status so the invoker knows the session is ready
		context.SetCustomStatus(new SessionTurnStatus
		{
			TurnId = string.Empty,
			Success = true,
			TurnCount = 0
		});

		while (turnCount < maxTurns)
		{
			var turnRequest = await context.WaitForExternalEventAsync<SessionTurnRequest>(TurnEventName);

			try
			{
				// Build the invocation with the prior conversation history
				var invocation = new DaprAgentInvocation(turnRequest.AgentName, turnRequest.Message, null, turnRequest.Options)
				{
					ChatClientKey = turnRequest.ChatClientKey,
					PriorMessages = conversationLog.Count > 0 ? conversationLog : null,
					TelemetryBaggage = turnRequest.TelemetryBaggage
				};

				// Dispatch as a child workflow - gets full durability
				var result = await context.CallChildWorkflowAsync<AgentRunResult>(nameof(AgentRunWorkflow), invocation);

				// Append this turn's messages to the session's conversation log
				conversationLog.AddRange(result.TurnMessages);
				turnCount++;

				// Write the response to the custom status so the caller can read it
				context.SetCustomStatus(new SessionTurnStatus
				{
					TurnId = turnRequest.TurnId,
					ResponseText = result.Response.Text,
					Success = true,
					TurnCount = turnCount
				});
			}
			catch (Exception ex)
			{
				turnCount++;
				context.SetCustomStatus(new SessionTurnStatus
				{
					TurnId = turnRequest.TurnId,
					Success = false,
					Error = ex.Message,
					TurnCount = turnCount
				});
			}
		}

		return $"Session completed after {turnCount} turns.";
	}
}
