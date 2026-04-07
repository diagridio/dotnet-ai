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

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Dapr.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RouterDemo.Models;
using RouterDemo.Workflows;

namespace RouterDemo.Agents;

internal static class RouterWorkflowAgentFactory
{
    private const string LoggerName = "RouterWorkflowAgent";

    public static AIAgent Create(IServiceProvider services, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var chatClient = services.GetRequiredKeyedService<IChatClient>(descriptor.ConversationComponentName);
        var innerAgent = chatClient.AsAIAgent(
            instructions: descriptor.Prompt,
            name: descriptor.Name,
            description: descriptor.Purpose);

        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        return innerAgent.AsBuilder()
            .Use(
                (messages, _, _, _, ct) => RunWorkflowAsync(scopeFactory, loggerFactory, messages, ct),
                (_, _, _, _, _) => System.Linq.AsyncEnumerable.Empty<AgentResponseUpdate>())
            .Build(services);
    }

    private static async Task<AgentResponse> RunWorkflowAsync(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        IEnumerable<ChatMessage> messages,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerName);
        var input = ExtractInput(messages);

        await using var scope = scopeFactory.CreateAsyncScope();
        var workflowClient = scope.ServiceProvider.GetRequiredService<DaprWorkflowClient>();
        var registry = scope.ServiceProvider.GetRequiredService<AgentRegistry>();
        var catalog = scope.ServiceProvider.GetRequiredService<AgentCatalog>();

        var availableAgents = catalog.GetRoutableAgents(registry);
        if (availableAgents.Count == 0)
        {
            return BuildResponse(CreateFailure("No routable agents are currently registered."));
        }

        var workflowInput = new AgentRouterWorkflow.RouterInput(input, availableAgents);

        try
        {
            var instanceId = await workflowClient.ScheduleNewWorkflowAsync(
                name: nameof(AgentRouterWorkflow),
                instanceId: null,
                input: workflowInput,
                startTime: null,
                cancellation: ct);

            var state = await workflowClient.WaitForWorkflowCompletionAsync(instanceId, cancellation: ct);
            if (state.RuntimeStatus != WorkflowRuntimeStatus.Completed)
            {
                var failure = state.FailureDetails?.ErrorMessage ?? "Workflow did not complete successfully.";
                logger.LogWarning(
                    "Router workflow {InstanceId} completed with status {Status}: {Failure}",
                    instanceId,
                    state.RuntimeStatus,
                    failure);
                return BuildResponse(CreateFailure($"Workflow status '{state.RuntimeStatus}': {failure}"));
            }

            var result = state.ReadOutputAs<AgentRouterWorkflow.RoutingResult>();
            if (string.IsNullOrWhiteSpace(result.Status))
            {
                return BuildResponse(CreateFailure("Workflow completed without a routing result."));
            }

            return BuildResponse(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Router workflow execution failed.");
            return BuildResponse(CreateFailure($"Router workflow execution failed: {ex.Message}"));
        }
    }

    private static string ExtractInput(IEnumerable<ChatMessage> messages)
    {
        var userText = string.Join(
            Environment.NewLine,
            messages
                .Where(message => message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
                .Select(message => message.Text));

        if (!string.IsNullOrWhiteSpace(userText))
        {
            return userText.Trim();
        }

        var fallbackText = string.Join(
            Environment.NewLine,
            messages
                .Select(message => message.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            return fallbackText.Trim();
        }

        throw new InvalidOperationException("Router workflow agent requires a non-empty input message.");
    }

    private static AgentResponse BuildResponse(AgentRouterWorkflow.RoutingResult result)
    {
        var payload = JsonSerializer.Serialize(result, AgentRouterJsonContext.Default.RoutingResult);
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, payload));
    }

    private static AgentRouterWorkflow.RoutingResult CreateFailure(string error)
    {
        return new AgentRouterWorkflow.RoutingResult(
            Status: "failed",
            AgentName: string.Empty,
            ModelComponent: string.Empty,
            ExpectedSchema: string.Empty,
            Result: null,
            RouterReason: "Router workflow failed before completion.",
            Attempts: new AgentRouterWorkflow.AttemptCounts(0, 0, 0),
            Error: error);
    }
}
