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

namespace Diagrid.AI.Microsoft.AgentFramework.Catalyst;

/// <summary>
/// Registry configuration information.
/// </summary>
public sealed class RegistryMetadata
{
	/// <summary>
	/// Dapr resource name backing the registry (e.g. the state store component).
	/// </summary>
	[JsonPropertyName("resource_name")]
	public string? ResourceName { get; init; } = "agent-registry";
	
	/// <summary>
	/// The logical team name the agent is registered under.
	/// </summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }
}
