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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that executes a single tool function, keeping each tool invocation as a discrete
/// Dapr Workflow activity for durability and observability.
/// </summary>
internal sealed partial class InvokeToolActivity(
    PendingFunctionRegistry registry,
    ILogger<InvokeToolActivity> logger) : WorkflowActivity<ToolInvocationInput, string>
{
    /// <inheritdoc />
    public override async Task<string> RunAsync(WorkflowActivityContext context, ToolInvocationInput input)
    {
        LogToolInvocationInfo(input.AgentName, input.FunctionName);
        LogToolInvocationDebug(input.AgentName, input.FunctionName, input.ArgumentsJson);

        var fn = registry.Get(input.CallId)
            ?? throw new InvalidOperationException(
                $"No pending function found for call ID '{input.CallId}' " +
                $"(agent: '{input.AgentName}', function: '{input.FunctionName}'). " +
                "This can happen if the process restarted between tool dispatch and activity execution.");

        var rawArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input.ArgumentsJson);
        var functionArgs = rawArgs is { Count: > 0 }
            ? new AIFunctionArguments(rawArgs.ToDictionary<KeyValuePair<string, JsonElement>, string, object?>(
                kv => kv.Key, kv => kv.Value))
            : new AIFunctionArguments();

        var result = await fn.InvokeAsync(functionArgs, CancellationToken.None).ConfigureAwait(false);

        var resultJson = JsonSerializer.Serialize(result);
        LogToolResultDebug(input.AgentName, input.FunctionName, resultJson);
        return resultJson;
    }

    [LoggerMessage(LogLevel.Information,
        "Invoking tool '{FunctionName}' for agent '{AgentName}'")]
    private partial void LogToolInvocationInfo(string agentName, string functionName);

    [LoggerMessage(LogLevel.Debug,
        "Invoking tool '{FunctionName}' for agent '{AgentName}' with arguments: {ArgumentsJson}")]
    private partial void LogToolInvocationDebug(string agentName, string functionName, string argumentsJson);

    [LoggerMessage(LogLevel.Debug,
        "Tool '{FunctionName}' for agent '{AgentName}' returned: {ResultJson}")]
    private partial void LogToolResultDebug(string agentName, string functionName, string resultJson);
}
