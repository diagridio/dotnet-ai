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
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// Extensions on <see cref="AIAgentBuilder"/> for Dapr-specific agent pipeline configuration.
/// </summary>
internal static class AIAgentBuilderExtensions
{
    /// <summary>
    /// Adds middleware to the agent pipeline that dispatches each tool invocation as a separate
    /// Dapr Workflow activity via <see cref="ToolRunWorkflow"/> / <see cref="InvokeToolActivity"/>,
    /// instead of executing it inline within the current activity.
    /// </summary>
    internal static AIAgentBuilder UseToolActivityDispatch(
        this AIAgentBuilder builder,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetRequiredService<PendingFunctionRegistry>();
        var workflowClient = services.GetRequiredService<DaprWorkflowClient>();

        return builder.Use(async (agent, ctx, _, ct) =>
        {
            var callId = registry.Register(ctx.Function);
            try
            {
                var argumentsJson = SerializeArguments(ctx.Arguments);
                var input = new ToolInvocationInput(
                    callId,
                    argumentsJson,
                    agent.Name ?? string.Empty,
                    ctx.Function.Name);

                var instanceId = await workflowClient
                    .ScheduleNewWorkflowAsync(
                        name: nameof(ToolRunWorkflow),
                        instanceId: null,
                        input: input,
                        startTime: null,
                        cancellation: ct)
                    .ConfigureAwait(false);

                var state = await workflowClient
                    .WaitForWorkflowCompletionAsync(instanceId, cancellation: ct)
                    .ConfigureAwait(false);

                if (state.RuntimeStatus != WorkflowRuntimeStatus.Completed)
                {
                    var error = state.FailureDetails?.ErrorMessage
                        ?? $"Tool workflow ended with status '{state.RuntimeStatus}'.";
                    throw new InvalidOperationException(
                        $"Tool '{ctx.Function.Name}' workflow did not complete successfully: {error}");
                }

                var resultJson = state.ReadOutputAs<string>()
                    ?? throw new InvalidOperationException(
                        $"Tool '{ctx.Function.Name}' workflow returned a null result.");

                // Return as JsonElement so the agent framework serializes it correctly into
                // the FunctionResultContent that is sent back to the LLM.
                return JsonSerializer.Deserialize<JsonElement>(resultJson);
            }
            finally
            {
                registry.Remove(callId);
            }
        });
    }

    private static string SerializeArguments(AIFunctionArguments arguments) =>
        arguments.Count == 0 ? "{}" :
            // Values coming from LLM tool call parsing are typically JsonElement already.
            // JsonSerializer handles JsonElement natively, so this is safe.
            JsonSerializer.Serialize(arguments);
}
