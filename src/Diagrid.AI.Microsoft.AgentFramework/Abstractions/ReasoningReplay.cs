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

using System.Text.Json.Serialization;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// How much of an agent's earlier reasoning is sent back to the model, and kept in the messages
/// the run carries and returns. Set it with <c>WithReasoningReplay(...)</c> on the agents builder.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReasoningReplay>))]
public enum ReasoningReplay
{
	/// <summary>
	/// The default. Reasoning is sent back on the run's later LLM calls and kept in
	/// <c>TurnMessages</c>, so a later turn given those messages as history continues from it.
	/// </summary>
	AcrossTurns = 0,

	/// <summary>
	/// Reasoning is sent back only on the LLM calls of the run that produced it, which providers
	/// such as Anthropic require inside a tool loop. It is left out of <c>TurnMessages</c>, so
	/// history does not grow with it from turn to turn.
	/// </summary>
	WithinRun = 1,

	/// <summary>
	/// Reasoning is never sent back, and the run's messages do not keep it. The response returned
	/// to the caller still starts with the final reasoning. Do not use it with providers that
	/// require the reasoning behind a tool call to accompany the tool result, such as Anthropic
	/// extended thinking: use <see cref="WithinRun"/> for those.
	/// </summary>
	Off = 2,
}
