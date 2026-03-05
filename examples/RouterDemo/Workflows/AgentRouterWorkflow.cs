// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using Dapr.Workflow;
using RouterDemo.Models;

namespace RouterDemo.Workflows;

/// <summary>
/// Workflow that delegates routing logic to a single activity to emphasize built-in agent workflows.
/// </summary>
public sealed partial class AgentRouterWorkflow : Workflow<AgentRouterWorkflow.RouterInput, AgentRouterWorkflow.RoutingResult>
{
    public override Task<RoutingResult> RunAsync(WorkflowContext context, RouterInput input) =>
        context.CallActivityAsync<RoutingResult>(nameof(RouteWithAgentActivity), input);

    public readonly record struct RouterInput(
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("availableAgents")] IReadOnlyList<AgentDescriptor> AvailableAgents);

    public readonly record struct RoutingResult(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("agentName")] string AgentName,
        [property: JsonPropertyName("modelComponent")] string ModelComponent,
        [property: JsonPropertyName("expectedSchema")] string ExpectedSchema,
        [property: JsonPropertyName("result")] JsonElement? Result,
        [property: JsonPropertyName("routerReason")] string RouterReason,
        [property: JsonPropertyName("attempts")] AttemptCounts Attempts,
        [property: JsonPropertyName("error")] string? Error);

    public readonly record struct AttemptCounts(
        [property: JsonPropertyName("router")] int Router,
        [property: JsonPropertyName("coordinator")] int Coordinator,
        [property: JsonPropertyName("agent")] int Agent);

    public readonly record struct RouterDecision(
        [property: JsonPropertyName("agentName")] string? AgentName,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("confidence")] double Confidence);

    public readonly record struct CoordinatorPlan(
        [property: JsonPropertyName("targetAgent")] string? TargetAgent,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("expectedSchema")] string? ExpectedSchema,
        [property: JsonPropertyName("retryNotes")] string? RetryNotes);

    public readonly record struct SummaryOutput(
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("bullets")] string[]? Bullets,
        [property: JsonPropertyName("confidence")] double Confidence);

    public readonly record struct ClassificationOutput(
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("tags")] string[]? Tags,
        [property: JsonPropertyName("confidence")] double Confidence);

    public readonly record struct PlanOutput(
        [property: JsonPropertyName("steps")] string[]? Steps,
        [property: JsonPropertyName("risks")] string? Risks,
        [property: JsonPropertyName("confidence")] double Confidence);
}
