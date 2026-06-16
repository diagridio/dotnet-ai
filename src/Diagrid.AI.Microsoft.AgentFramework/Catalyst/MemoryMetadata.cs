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
/// Memory configuration information.
/// </summary>
public sealed record MemoryMetadata
{
	/// <summary>
	/// Short-term conversation memory store.
	/// </summary>
	[JsonPropertyName("short_term")]
	public MemoryStoreMetadata? ShortTerm { get; init; }
	
	/// <summary>
	/// Long-term conversation memory store.
	/// </summary>
	[JsonPropertyName("long_term")]
	public MemoryStoreMetadata? LongTerm { get; init; }
}

/// <summary>
/// Metadata about a single memory backing store.
/// </summary>
/// <param name="Type">Implementation class name.</param>
public sealed record MemoryStoreMetadata([property: JsonPropertyName("type")] string Type)
{
	/// <summary>
	/// Dapr resource name for this store.
	/// </summary>
	[JsonPropertyName("resource_name")]
	public string? ResourceName { get; init; }
}