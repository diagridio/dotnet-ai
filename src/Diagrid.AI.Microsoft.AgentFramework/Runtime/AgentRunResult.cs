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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Extended result from <see cref="AgentRunWorkflow"/> that includes the conversation messages produced
/// during this run, in addition to the final <see cref="AgentResponse"/>. This is used by the
/// <see cref="SessionWorkflow"/> to accumulate the conversation log.
/// </summary>
public sealed record AgentRunResult
{
	/// <summary>
	/// The final agent response.
	/// </summary>
	public AgentResponse Response { get; init; } = null!;

	/// <summary>
	/// The messages produced during THIS turn only (excludes prior messages).
	/// Includes the user message, any LLM messages, tool calls, and tool results.
	/// </summary>
	public List<WorkflowChatMessage> TurnMessages { get; init; } = [];
}