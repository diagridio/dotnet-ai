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
/// Input for <see cref="SessionWorkflow"/>. Currently minimal, but can be extended with session-level
/// configuration in the future (e.g. default agent, shared system prompt, etc.).
/// </summary>
public sealed record SessionWorkflowInput
{
	private readonly uint _maxTurns;

	/// <summary>
	/// Optional number of turns before the session workflow completes. Defaults to 200.
	/// </summary>
	public uint? MaxTurns
	{
		get => _maxTurns;
		init => _maxTurns = value ?? 200;
	}
}