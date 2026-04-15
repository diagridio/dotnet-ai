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
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that executes a single tool function. The tool is resolved from the persistent
/// <see cref="ToolRegistry"/> by agent name and function name, so it survives process restarts.
/// Each tool invocation is checkpointed by Dapr Workflows — on crash recovery, completed
/// tool activities are NOT re-executed.
/// </summary>
internal sealed partial class ExecuteToolActivity(
    ToolRegistry toolRegistry,
    AgentRegistry agentRegistry,
    IDaprAgentContextAccessor contextAccessor,
    DaprWorkflowClient workflowClient,
    IServiceProvider serviceProvider,
    ILogger<ExecuteToolActivity> logger) : WorkflowActivity<ExecuteToolInput, ExecuteToolOutput>
{
    /// <inheritdoc />
    public override async Task<ExecuteToolOutput> RunAsync(WorkflowActivityContext context, ExecuteToolInput input)
    {
        LogToolInvocationInfo(input.AgentName, input.FunctionName);
        LogToolInvocationDebug(input.AgentName, input.FunctionName, input.ArgumentsJson);

        var fn = toolRegistry.Get(input.AgentName, input.FunctionName);
        if (fn is null)
        {
            // Trigger lazy agent resolution — this runs the factory which
            // populates ToolRegistry as a side effect.
            agentRegistry.Get(input.AgentName, serviceProvider);
            fn = toolRegistry.Get(input.AgentName, input.FunctionName);
        }

        if (fn is null)
        {
            throw new InvalidOperationException(
                $"Tool '{input.FunctionName}' not found in registry for agent '{input.AgentName}'. " +
                "Ensure the agent was registered with tools via AddDaprAgents().WithAgent(...).");
        }

        // Safe for concurrent activities: DaprAgentContextAccessor uses AsyncLocal,
        // so each activity's async flow sees its own value (see DaprAgentContextAccessor remarks).
        contextAccessor.Current = new DaprAgentContext(workflowClient, context.InstanceId);
        try
        {
            var rawArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input.ArgumentsJson);
            var functionArgs = rawArgs is { Count: > 0 }
                ? new AIFunctionArguments(rawArgs.ToDictionary<KeyValuePair<string, JsonElement>, string, object?>(
                    kv => kv.Key, kv => kv.Value))
                : new AIFunctionArguments();

            var result = await fn.InvokeAsync(functionArgs, CancellationToken.None).ConfigureAwait(false);
            var resultJson = JsonSerializer.Serialize(result);

            LogToolResultDebug(input.AgentName, input.FunctionName, resultJson);

            return new ExecuteToolOutput
            {
                CallId = input.CallId,
                FunctionName = input.FunctionName,
                ResultJson = resultJson
            };
        }
        catch (Exception ex)
        {
            LogToolError(input.AgentName, input.FunctionName, ex.Message);
            throw;
        }
        finally
        {
            contextAccessor.Current = null;
        }
    }

    [LoggerMessage(LogLevel.Information, "Invoking tool '{FunctionName}' for agent '{AgentName}'")]
    private partial void LogToolInvocationInfo(string agentName, string functionName);

    [LoggerMessage(LogLevel.Debug,
        "Invoking tool '{FunctionName}' for agent '{AgentName}' with arguments: {ArgumentsJson}")]
    private partial void LogToolInvocationDebug(string agentName, string functionName, string argumentsJson);

    [LoggerMessage(LogLevel.Debug, "Tool '{FunctionName}' for agent '{AgentName}' returned: {ResultJson}")]
    private partial void LogToolResultDebug(string agentName, string functionName, string resultJson);

    [LoggerMessage(LogLevel.Error, "Tool '{FunctionName}' for agent '{AgentName}' failed: {ErrorMessage}")]
    private partial void LogToolError(string agentName, string functionName, string errorMessage);
}
