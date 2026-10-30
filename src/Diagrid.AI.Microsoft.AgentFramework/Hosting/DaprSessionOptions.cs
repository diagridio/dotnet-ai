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

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Settings for a session created with <see cref="DaprSessionExtensions.CreateStoredSessionAsync"/>.
/// </summary>
public sealed class DaprSessionOptions
{
	/// <summary>
	/// The number of turns before the session workflow completes, or <see langword="null"/> for 200.
	/// </summary>
	public uint? MaxTurns { get; set; }

	/// <summary>
	/// The stored conversation to continue, or <see langword="null"/> to use the session's own id. Set it to
	/// continue a conversation from an earlier session, for example after that one reached <see cref="MaxTurns"/>.
	/// At most 256 characters.
	/// </summary>
	public string? ConversationId { get; set; }
}
