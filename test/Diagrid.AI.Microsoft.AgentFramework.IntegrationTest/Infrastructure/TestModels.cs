// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json.Serialization;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>Request body for /ask endpoints.</summary>
public sealed record AskRequest(string Prompt, string? AgentName = null);

/// <summary>Response body returned by /ask.</summary>
public sealed record AskResponse(string Response);

/// <summary>Typed payload returned by /ask-typed, matching the capital-answer schema used in AgentInvokerDemo.</summary>
public sealed record CapitalAnswer(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("confidence")] double Confidence);

/// <summary>Source-generated JSON serialization context for all integration-test DTOs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AskRequest))]
[JsonSerializable(typeof(AskResponse))]
[JsonSerializable(typeof(CapitalAnswer))]
public partial class IntegrationTestJsonContext : JsonSerializerContext;
