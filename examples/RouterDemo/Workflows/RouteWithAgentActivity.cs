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

using System.Text;
using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Dapr.Workflow;
using RouterDemo.Models;
using AttemptCounts = RouterDemo.Workflows.AgentRouterWorkflow.AttemptCounts;
using ClassificationOutput = RouterDemo.Workflows.AgentRouterWorkflow.ClassificationOutput;
using CoordinatorPlan = RouterDemo.Workflows.AgentRouterWorkflow.CoordinatorPlan;
using PlanOutput = RouterDemo.Workflows.AgentRouterWorkflow.PlanOutput;
using RouterDecision = RouterDemo.Workflows.AgentRouterWorkflow.RouterDecision;
using RouterInput = RouterDemo.Workflows.AgentRouterWorkflow.RouterInput;
using RoutingResult = RouterDemo.Workflows.AgentRouterWorkflow.RoutingResult;
using SummaryOutput = RouterDemo.Workflows.AgentRouterWorkflow.SummaryOutput;

namespace RouterDemo.Workflows;

/// <summary>
/// Activity that performs routing and agent coordination using the workflow-backed agent invoker.
/// </summary>
internal sealed partial class RouteWithAgentActivity(
    IDaprAgentInvoker invoker,
    ILogger<RouteWithAgentActivity> logger) : WorkflowActivity<RouterInput, RoutingResult>
{
    private const int MaxRouterAttempts = 2;
    private const int MaxCoordinatorAttempts = 2;
    private const int MaxAgentAttempts = 3;

    public override async Task<RoutingResult> RunAsync(WorkflowActivityContext context, RouterInput input)
    {
        var availableAgents = input.AvailableAgents.ToArray() ?? [];
        if (availableAgents.Length == 0)
            throw new InvalidOperationException("No routable agents were supplied to the workflow.");

        var routerAgent = invoker.GetAgent(AgentIds.RouterName, AgentIds.TinyComponent);
        var coordinatorAgent = invoker.GetAgent(AgentIds.CoordinatorName, AgentIds.TinyComponent);

        var (decision, routerAttempts, routerError) = await GetRouterDecisionAsync(
            routerAgent,
            availableAgents,
            input.Input);

        var selectedAgent = ResolveSelectedAgent(availableAgents, decision, logger);
        var coordinatorPlan = await GetCoordinatorPlanAsync(
            coordinatorAgent,
            selectedAgent,
            input.Input,
            decision,
            logger,
            routerError,
            attemptOverride: null);

        var coordinatorAttempts = 1;
        var agentAttempts = 0;
        string? lastError = null;

        while (agentAttempts < MaxAgentAttempts)
        {
            agentAttempts++;
            var result = await TryInvokeTargetAsync(
                selectedAgent,
                coordinatorPlan,
                input.Input);

            if (result is { IsValid: true, Payload: not null })
            {
                return new RoutingResult(
                    Status: "completed",
                    AgentName: selectedAgent.Name,
                    ModelComponent: selectedAgent.ConversationComponentName,
                    ExpectedSchema: coordinatorPlan.ExpectedSchema ?? selectedAgent.OutputSchema,
                    Result: result.Payload,
                    RouterReason: decision.Reason ?? "Routed without a reason.",
                    Attempts: new AttemptCounts(routerAttempts, coordinatorAttempts, agentAttempts),
                    Error: null);
            }

            lastError = result.Error ?? "The agent response did not match the expected schema.";
            LogAgentRetry(logger, selectedAgent.Name, agentAttempts, lastError);

            if (coordinatorAttempts >= MaxCoordinatorAttempts)
            {
                break;
            }

            coordinatorAttempts++;
            coordinatorPlan = await GetCoordinatorPlanAsync(
                coordinatorAgent,
                selectedAgent,
                input.Input,
                decision,
                logger,
                lastError,
                attemptOverride: coordinatorAttempts);
        }

        return new RoutingResult(
            Status: "failed",
            AgentName: selectedAgent.Name,
            ModelComponent: selectedAgent.ConversationComponentName,
            ExpectedSchema: coordinatorPlan.ExpectedSchema ?? selectedAgent.OutputSchema,
            Result: null,
            RouterReason: decision.Reason ?? "Routed without a reason.",
            Attempts: new AttemptCounts(routerAttempts, coordinatorAttempts, agentAttempts),
            Error: lastError ?? "Unable to obtain a valid response from the routed agent.");
    }

    private static AgentDescriptor ResolveSelectedAgent(
        AgentDescriptor[] availableAgents,
        RouterDecision decision,
        ILogger logger)
    {
        var selected = availableAgents.FirstOrDefault(agent =>
            string.Equals(agent.Name, decision.AgentName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            return selected;
        }

        var fallback = availableAgents[0];
        LogRouterFallback(logger, decision.AgentName ?? "(missing)", fallback.Name);
        return fallback;
    }

    private async Task<(RouterDecision Decision, int Attempts, string? Error)> GetRouterDecisionAsync(
        IDaprAIAgent routerAgent,
        AgentDescriptor[] availableAgents,
        string input)
    {
        RouterDecision? decision = null;
        var attempts = 0;
        string? lastError = null;

        for (var attempt = 1; attempt <= MaxRouterAttempts; attempt++)
        {
            attempts = attempt;
            var message = BuildRouterMessage(availableAgents, input, lastError, attempt);
            try
            {
                decision = await invoker.RunAgentAndDeserializeAsync<RouterDecision>(
                    routerAgent,
                    logger,
                    message: message);
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                LogRouterInvalid(logger, attempt, lastError);
                continue;
            }

            if (!decision.HasValue)
            {
                lastError = "Router returned an empty response.";
                LogRouterInvalid(logger, attempt, lastError);
                continue;
            }

            if (string.IsNullOrWhiteSpace(decision.Value.AgentName))
            {
                lastError = "Router response missing agentName.";
                LogRouterInvalid(logger, attempt, lastError);
                continue;
            }

            if (availableAgents.Any(agent =>
                    string.Equals(agent.Name, decision.Value.AgentName, StringComparison.OrdinalIgnoreCase)))
            {
                return (decision.Value, attempts, null);
            }

            lastError = $"Router selected an unknown agent '{decision.Value.AgentName}'.";
            LogRouterInvalid(logger, attempt, lastError);
        }

        var fallback = availableAgents[0];
        var fallbackDecision = new RouterDecision(
            AgentName: fallback.Name,
            Reason: $"Fallback to '{fallback.Name}' after router errors: {lastError}",
            Confidence: 0.0);

        return (fallbackDecision, attempts, lastError);
    }

    private async Task<CoordinatorPlan> GetCoordinatorPlanAsync(
        IDaprAIAgent coordinatorAgent,
        AgentDescriptor targetAgent,
        string input,
        RouterDecision decision,
        ILogger logger,
        string? priorError,
        int? attemptOverride)
    {
        CoordinatorPlan? plan;
        var attempts = 0;
        string? lastError = priorError;

        for (var attempt = 1; attempt <= MaxCoordinatorAttempts; attempt++)
        {
            attempts = attempt;
            var message = BuildCoordinatorMessage(targetAgent, input, decision, lastError, attemptOverride ?? attempt);
            try
            {
                plan = await invoker.RunAgentAndDeserializeAsync<CoordinatorPlan>(
                    coordinatorAgent,
                    logger,
                    message: message);
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                LogCoordinatorInvalid(logger, attempt, lastError);
                continue;
            }

            if (!plan.HasValue || string.IsNullOrWhiteSpace(plan.Value.TargetAgent) || string.IsNullOrWhiteSpace(plan.Value.Message))
            {
                lastError = "Coordinator response missing required fields.";
                LogCoordinatorInvalid(logger, attempt, lastError);
                continue;
            }

            if (!string.Equals(plan.Value.TargetAgent, targetAgent.Name, StringComparison.OrdinalIgnoreCase))
            {
                lastError = $"Coordinator targeted '{plan.Value.TargetAgent}', expected '{targetAgent.Name}'.";
                LogCoordinatorInvalid(logger, attempt, lastError);
                continue;
            }

            if (string.IsNullOrWhiteSpace(plan.Value.ExpectedSchema))
            {
                lastError = "Coordinator response missing expectedSchema.";
                LogCoordinatorInvalid(logger, attempt, lastError);
                continue;
            }

            return plan.Value;
        }

        LogCoordinatorFallback(logger, attempts, lastError ?? "Unknown coordinator error.");
        return new CoordinatorPlan(
            TargetAgent: targetAgent.Name,
            Message: BuildFallbackCoordinatorMessage(targetAgent, lastError),
            ExpectedSchema: targetAgent.OutputSchema,
            RetryNotes: lastError ?? "Fallback coordinator plan.");
    }

    private async Task<InvocationResult> TryInvokeTargetAsync(
        AgentDescriptor targetAgent,
        CoordinatorPlan plan,
        string input)
    {
        var agent = invoker.GetAgent(targetAgent.Name, targetAgent.ConversationComponentName);
        var message = BuildTargetMessage(plan, input);

        try
        {
            switch (targetAgent.OutputKind)
            {
                case AgentOutputKind.Summary:
                    var summary = await invoker.RunAgentAndDeserializeAsync<SummaryOutput>(agent, logger, message);
                    return ValidateSummary(summary);
                case AgentOutputKind.Classification:
                    var classification = await invoker.RunAgentAndDeserializeAsync<ClassificationOutput>(agent, logger, message);
                    return ValidateClassification(classification);
                case AgentOutputKind.Plan:
                    var planOutput = await invoker.RunAgentAndDeserializeAsync<PlanOutput>(agent, logger, message);
                    return ValidatePlan(planOutput);
                default:
                    return new InvocationResult(false, null, $"Unknown output kind {targetAgent.OutputKind}.");
            }
        }
        catch (JsonException ex)
        {
            return new InvocationResult(false, null, ex.Message);
        }
    }

    private static InvocationResult ValidateSummary(SummaryOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.Summary))
            return new InvocationResult(false, null, "Summary was missing.");

        if (output.Bullets is null || output.Bullets.Length == 0)
            return new InvocationResult(false, null, "Bullets were missing.");

        var payload = JsonSerializer.SerializeToElement(output, AgentRouterJsonContext.Default.SummaryOutput);
        return new InvocationResult(true, payload, null);
    }

    private static InvocationResult ValidateClassification(ClassificationOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.Category))
            return new InvocationResult(false, null, "Category was missing.");

        if (output.Tags is null || output.Tags.Length == 0)
            return new InvocationResult(false, null, "Tags were missing.");

        var payload = JsonSerializer.SerializeToElement(output, AgentRouterJsonContext.Default.ClassificationOutput);
        return new InvocationResult(true, payload, null);
    }

    private static InvocationResult ValidatePlan(PlanOutput output)
    {
        if (output.Steps is null || output.Steps.Length == 0)
            return new InvocationResult(false, null, "Steps were missing.");

        if (string.IsNullOrWhiteSpace(output.Risks))
            return new InvocationResult(false, null, "Risks were missing.");

        var payload = JsonSerializer.SerializeToElement(output, AgentRouterJsonContext.Default.PlanOutput);
        return new InvocationResult(true, payload, null);
    }

    private static string BuildRouterMessage(
        AgentDescriptor[] availableAgents,
        string input,
        string? lastError,
        int attempt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a routing agent.");
        builder.AppendLine("Select the best specialist agent for the user request.");
        builder.AppendLine("Only choose from the provided agents and return JSON only.");
        builder.AppendLine();
        builder.AppendLine("Return JSON schema:");
        builder.AppendLine("{\"agentName\":\"string\",\"reason\":\"string\",\"confidence\":0.0}");
        builder.AppendLine();
        builder.AppendLine("Available agents:");

        foreach (var agent in availableAgents)
        {
            builder.AppendLine($"- Name: {agent.Name}");
            builder.AppendLine($"  Purpose: {agent.Purpose}");
            builder.AppendLine($"  Prompt: {agent.Prompt}");
            builder.AppendLine($"  Output schema: {agent.OutputSchema}");
        }

        builder.AppendLine();
        builder.AppendLine($"User request: {input}");

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            builder.AppendLine();
            builder.AppendLine($"Previous routing issue (attempt {attempt - 1}): {lastError}");
            builder.AppendLine("Fix the JSON and ensure agentName matches the list.");
        }

        return builder.ToString();
    }

    private static string BuildCoordinatorMessage(
        AgentDescriptor targetAgent,
        string input,
        RouterDecision decision,
        string? priorError,
        int attempt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a coordinator agent.");
        builder.AppendLine("Create the exact message to send to the target agent.");
        builder.AppendLine("Do not include the user input in the message; it will be appended later.");
        builder.AppendLine("Return JSON only.");
        builder.AppendLine();
        builder.AppendLine("Return JSON schema:");
        builder.AppendLine("{\"targetAgent\":\"string\",\"message\":\"string\",\"expectedSchema\":\"string\",\"retryNotes\":\"string\"}");
        builder.AppendLine();
        builder.AppendLine($"Target agent: {targetAgent.Name}");
        builder.AppendLine($"Target prompt: {targetAgent.Prompt}");
        builder.AppendLine($"Expected schema: {targetAgent.OutputSchema}");
        builder.AppendLine($"Routing rationale: {decision.Reason}");
        builder.AppendLine($"Attempt: {attempt}");

        if (!string.IsNullOrWhiteSpace(priorError))
        {
            builder.AppendLine($"Previous error: {priorError}");
            builder.AppendLine("Tighten the output instructions to ensure valid JSON.");
        }

        builder.AppendLine();
        builder.AppendLine($"User request: {input}");
        return builder.ToString();
    }

    private static string BuildFallbackCoordinatorMessage(AgentDescriptor targetAgent, string? error)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Return JSON only.");
        builder.AppendLine($"Schema: {targetAgent.OutputSchema}");
        builder.AppendLine("If any field is missing, respond with empty strings or empty arrays.");

        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.AppendLine($"Previous issue: {error}");
        }

        return builder.ToString();
    }

    private static string BuildTargetMessage(CoordinatorPlan plan, string input)
    {
        var schemaLine = string.IsNullOrWhiteSpace(plan.ExpectedSchema)
            ? string.Empty
            : $"Expected schema: {plan.ExpectedSchema}{Environment.NewLine}";

        return $"{plan.Message}{Environment.NewLine}{schemaLine}{Environment.NewLine}User request:{Environment.NewLine}{input}";
    }

    [LoggerMessage(LogLevel.Warning, "Router attempt {Attempt} returned invalid output: {Reason}")]
    private static partial void LogRouterInvalid(ILogger logger, int attempt, string reason);

    [LoggerMessage(LogLevel.Warning, "Coordinator attempt {Attempt} returned invalid output: {Reason}")]
    private static partial void LogCoordinatorInvalid(ILogger logger, int attempt, string reason);

    [LoggerMessage(LogLevel.Warning, "Coordinator fallback after {Attempts} attempts: {Reason}")]
    private static partial void LogCoordinatorFallback(ILogger logger, int attempts, string reason);

    [LoggerMessage(LogLevel.Warning, "Router selected unknown agent '{Selected}', falling back to '{Fallback}'.")]
    private static partial void LogRouterFallback(ILogger logger, string selected, string fallback);

    [LoggerMessage(LogLevel.Warning, "Retrying agent '{AgentName}' after attempt {Attempt}: {Reason}")]
    private static partial void LogAgentRetry(ILogger logger, string agentName, int attempt, string reason);

    private readonly record struct InvocationResult(bool IsValid, JsonElement? Payload, string? Error);
}
