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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// The custom status written by the <see cref="SessionWorkflow"/> after each turn. The
/// invoker polls for this to retrieve the response.
/// </summary>
internal sealed record SessionTurnStatus
{
	/// <summary>
	/// The correlation ID matching the <see cref="SessionTurnRequest"/>.
	/// </summary>
	public string TurnId { get; init; } = string.Empty;
	
	/// <summary>
	/// The final response text from the agent.
	/// </summary>
	public string? ResponseText { get; init; }
	
	/// <summary>
	/// Whether the turn completed successfully.
	/// </summary>
	public bool Success { get; init; }
	
	/// <summary>
	/// The error message if the turn failed.
	/// </summary>
	public string? Error { get; init; }
	
	/// <summary>
	/// The total number of turns completed in this session.
	/// </summary>
	public int TurnCount { get; init; }
}