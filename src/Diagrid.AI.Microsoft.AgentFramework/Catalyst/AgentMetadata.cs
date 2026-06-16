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
/// Metadata about an agent's configuration and capabilities.
/// </summary>
public sealed class AgentMetadata
{
	/// <summary>
	/// The Dapr application ID (APP_ID) of the sidecar.
	/// </summary>
	[JsonPropertyName("appid")]
	public required string AppId { get; init; }

	/// <summary>
	/// The type of the agent (e.g. standalone, durable)
	/// </summary>
	[JsonPropertyName("type")]
	public required string Type { get; init; } = "durable";
	
	/// <summary>
	/// Indicates if the agent if an orchestrator.
	/// </summary>
	[JsonPropertyName("orchestrator")]
	public bool Orchestrator { get; init; } = false;
	
	/// <summary>
	/// Role of the agent.
	/// </summary>
	[JsonPropertyName("role")]
	public string? Role { get; init; }
	
	/// <summary>
	/// High-level objective of the agent.
	/// </summary>
	[JsonPropertyName("goal")]
	public string? Goal { get; init; }
		
	/// <summary>
	/// Instructions for the agent.
	/// </summary>
	[JsonPropertyName("instructions")]
	public List<string> Instructions { get; init; } = [];
	
	/// <summary>
	/// System prompt guiding the agent's behavior.
	/// </summary>
	[JsonPropertyName("system_prompt")]
	public string? SystemPrompt { get; init; }

	/// <summary>
	/// The framework or library the agent is built with.
	/// </summary>
	[JsonPropertyName("framework")]
	public string? Framework { get; init; } = "Microsoft Agent Framework";
	
	/// <summary>
	/// The maximum iterations for agent execution.
	/// </summary>
	[JsonPropertyName("max_iterations")]
	public int? MaxIterations { get; init; }
	
	/// <summary>
	/// The tool choice strategy.
	/// </summary>
	[JsonPropertyName("tool_choice")]
	public string? ToolChoice { get; init; }

	/// <summary>
	/// Additional user-supplied metadata about the agent.
	/// </summary>
	[JsonPropertyName("metadata")]
	public Dictionary<string, object> Metadata { get; init; } = [];
	
	
	
	
}