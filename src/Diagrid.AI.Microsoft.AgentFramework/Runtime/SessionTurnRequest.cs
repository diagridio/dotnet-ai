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
/// Payment sent as an external event to a running <see cref="SessionWorkflow"/> representing
/// a single conversational turn.
/// </summary>
internal sealed record SessionTurnRequest
{
	/// <summary>
	/// The name of the agent to invoke for this turn.
	/// </summary>
	public string AgentName { get; init; } = string.Empty;
	
	/// <summary>
	/// Optional chat client key for keyed agent resolution.
	/// </summary>
	public string? ChatClientKey { get; init; }
	
	/// <summary>
	/// The user message for this turn.
	/// </summary>
	public string? Message { get; init; }

	/// <summary>
	/// A unique correlation ID for this turn, used to match the response in the workflow's
	/// custom status.
	/// </summary>
	public string TurnId { get; init; } = string.Empty;
	
	/// <summary>
	/// Options relevant to performing the agent run operation.
	/// </summary>
	public AgentRunOptions? Options { get; init; }
}