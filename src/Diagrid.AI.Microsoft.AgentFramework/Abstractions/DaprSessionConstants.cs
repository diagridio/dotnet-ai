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

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Well-known keys used in <see cref="AgentSession.StateBag"/> for Dapr Workflow-based sessions.
/// </summary>
public static class DaprSessionConstants
{
	/// <summary>
	/// The key under whcih the session workflow instance ID is stored in the StateBag.
	/// </summary>
	public const string SessionInstanceIdKey = "dapr.session.instanceId";
}