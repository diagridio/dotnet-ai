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
/// Metadata about the LLM used by the agent.
/// </summary>
/// <param name="Client">The LLM client used by the agent.</param>
/// <param name="Provider">The LLM provider used by the agent.</param>
public sealed record LlmMetadata(
	[property: JsonPropertyName("client")] string Client, 
	[property: JsonPropertyName("provider")] string Provider)
{
	/// <summary>
	/// The API type used by the LLM client.
	/// </summary>
	[JsonPropertyName("api")]
	public string Api { get; init; } = "unknown";

	/// <summary>
	/// The model name or identifier.
	/// </summary>
	[JsonPropertyName("model")]
	public string Model { get; init; } = "unknown";
	
	/// <summary>
	/// Dapr resource name for the LLM client.
	/// </summary>
	[JsonPropertyName("resource_name")]
	public string? ResourceName { get; init; }
	
	/// <summary>
	/// The base URL for the LLM API if applicable.
	/// </summary>
	[JsonPropertyName("base_url")]
	public string? BaseUrl { get; init; }
	
	/// <summary>
	/// The Azure endpoint if using Azure OpenAI.
	/// </summary>
	[JsonPropertyName("azure_endpoint")]
	public string? AzureEndpoint { get; init; }
	
	/// <summary>
	/// Azure deployment name if using Azure OpenAI.
	/// </summary>
	[JsonPropertyName("azure_deployment")]
	public string? AzureDeployment { get; init; }
	
	/// <summary>
	/// The prompt template used by the agent.
	/// </summary>
	[JsonPropertyName("prompt_template")]
	public string? PromptTemplate { get; init; }
}