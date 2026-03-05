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

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that executes a single agent run (LLM/tool calls, I/O), keeping the workflow deterministic.
/// </summary>
public sealed partial class InvokeAgentActivity(
    AgentRegistry registry,
    DaprWorkflowClient client,
    IServiceProvider services,
    ILogger<InvokeAgentActivity> logger,
    IDaprAgentContextAccessor contextAccessor) : WorkflowActivity<DaprAgentInvocation, AgentRunResponse>
{
    /// <inheritdoc />
    public override async Task<AgentRunResponse> RunAsync(WorkflowActivityContext context, DaprAgentInvocation input)
    {
        // Resolve the registry and agent lazily
        var agent = registry.Get(input.AgentName, input.ChatClientKey, services);

        // Ambient context for tools
        contextAccessor.Current = new DaprAgentContext(client, context.InstanceId);

        try
        {
            var message = input.Message ?? string.Empty;
            LogAgentInvocationInfo(
                input.AgentName,
                message.Length,
                input.Thread is not null,
                input.Options is not null);
            LogAgentInvocationDebug(input.AgentName, message, input.Thread, input.Options);
            return await agent.RunAsync(
                message: message,
                thread: input.Thread,
                options: input.Options).ConfigureAwait(false);
        }
        finally
        {
            contextAccessor.Current = null;
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Invoking agent {AgentName} with message length {MessageLength} (has thread {HasThread}, has options {HasOptions})")]
    private partial void LogAgentInvocationInfo(
        string agentName,
        int messageLength,
        bool hasThread,
        bool hasOptions);

    [LoggerMessage(LogLevel.Debug,
        "Invoking agent {AgentName} with message '{Message}' on thread '{Thread}' with options '{Options}'")]
    private partial void LogAgentInvocationDebug(string agentName, string message, AgentThread? thread,
        AgentRunOptions? options);
}
