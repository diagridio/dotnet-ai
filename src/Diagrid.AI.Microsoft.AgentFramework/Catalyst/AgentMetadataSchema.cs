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
/// Schema for agent metadata including schema version.
/// </summary>
/// <param name="Version">Version of the schema used for the agent metadata.</param>
/// <param name="Agent">Agent configuration and capabilities.</param>
/// <param name="Name">Logical agent name used as the registry key; distinct from agent.appid.</param>
public sealed record AgentMetadataSchema(
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("agent")] AgentMetadata Agent,
	[property: JsonPropertyName("name")] string Name)
{
	/// <summary>
	/// ISO 8601 timestamp of registration.
	/// </summary>
	[JsonPropertyName("registered_at")]
	public string RegisteredAt { get; init; } = DateTime.UtcNow.ToString("O");
	
	/// <summary>
	/// Pub/sub configuration, if enabled.
	/// </summary>
	[JsonPropertyName("pubsub")]
	public PubSubMetadata? PubSub { get; init; }
	
	/// <summary>
	/// Memory configuration, if enabled.
	/// </summary>
	public MemoryMetadata? Memory { get; init; }
	
	/// <summary>
	/// LLM configuration.
	/// </summary>
	[JsonPropertyName("llm")]
	public LlmMetadata? Llm { get; init; }
	
	/// <summary>
	/// Registry configuration.
	/// </summary>
	[JsonPropertyName("registry")]
	public RegistryMetadata? Registry { get; init; }

	/// <summary>
	/// Available tools.
	/// </summary>
	[JsonPropertyName("tools")]
	public List<ToolMetadata> Tools { get; init; } = [];
}
