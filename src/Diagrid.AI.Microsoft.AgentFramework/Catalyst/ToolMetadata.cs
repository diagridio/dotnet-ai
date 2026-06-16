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
/// Metadata about a tool available to the agent.
/// </summary>
/// <param name="Name">The name of the tool.</param>
/// <param name="Description">The description of the tool's functionality.</param>
/// <param name="Arguments">Arguments for the tool.</param>
public sealed record ToolMetadata(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("arguments")] string Arguments);
