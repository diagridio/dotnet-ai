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

using Dapr.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Durable workflow that orchestrates an agent run. Each LLM call and each tool invocation
/// is scheduled as a separate Dapr Workflow activity so that on crash recovery, already-completed
/// activities are NOT replayed.
/// <list type="number">
///   <item>Call the LLM as an activity (<see cref="CallLlmActivity"/>)</item>
///   <item>If the LLM returns tool calls, execute each tool as a separate activity (<see cref="ExecuteToolActivity"/>)</item>
///   <item>Feed tool results back and loop until the LLM returns a final response</item>
/// </list>
/// </summary>
public sealed class AgentRunWorkflow : Workflow<DaprAgentInvocation, AgentResponse>
{
    private const int MaxIterations = 20;

    /// <inheritdoc />
    public override async Task<AgentResponse> RunAsync(WorkflowContext context, DaprAgentInvocation input)
    {
        if (string.IsNullOrWhiteSpace(input.Message))
        {
            throw new ArgumentException(
                "Message cannot be null or whitespace.", nameof(input));
        }

        var messages = new List<WorkflowChatMessage>
        {
            new() { Role = "user", Content = input.Message }
        };

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            // Activity: Call the LLM once.
            var llmInput = new CallLlmInput(input.AgentName, input.ChatClientKey, messages);
            var llmOutput = await context.CallActivityAsync<CallLlmOutput>(
                nameof(CallLlmActivity), llmInput);

            if (llmOutput.Error is not null)
            {
                throw new InvalidOperationException(
                    $"LLM call failed for agent '{input.AgentName}': {llmOutput.Error}");
            }

            // If this is a final response (no tool calls), we're done.
            if (llmOutput.IsFinal)
            {
                return new AgentResponse(new ChatMessage(ChatRole.Assistant, llmOutput.Text ?? string.Empty));
            }

            // Add the assistant message (with tool calls) to the conversation.
            messages.Add(new WorkflowChatMessage
            {
                Role = "assistant",
                Content = llmOutput.Text,
                FunctionCalls = llmOutput.FunctionCalls
            });

            // Activity: Execute each tool call as a separate activity.
            var toolResults = new List<ExecuteToolOutput>();
            foreach (var fc in llmOutput.FunctionCalls!)
            {
                var toolInput = new ExecuteToolInput(input.AgentName, fc.Name, fc.CallId, fc.ArgumentsJson);
                var toolOutput = await context.CallActivityAsync<ExecuteToolOutput>(
                    nameof(ExecuteToolActivity), toolInput);
                toolResults.Add(toolOutput);
            }

            // Add tool results to the conversation.
            messages.Add(new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults = toolResults.Select(r => new WorkflowFunctionResult
                {
                    CallId = r.CallId,
                    Name = r.FunctionName,
                    ResultJson = r.ResultJson
                }).ToList()
            });
        }

        throw new InvalidOperationException(
            $"Agent '{input.AgentName}' exceeded the maximum of {MaxIterations} iterations.");
    }
}
