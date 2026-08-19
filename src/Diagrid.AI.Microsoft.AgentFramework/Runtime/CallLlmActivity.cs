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
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that performs a single LLM call. Each invocation is checkpointed by Dapr Workflows,
/// so on crash recovery the result is replayed without re-executing the LLM call.
/// </summary>
/// <remarks>
/// In addition to the agent's statically-registered instructions/tools (from <see cref="ChatClientRegistry"/>),
/// this activity layers in the per-run contribution computed once by <see cref="ResolveAgentContextActivity"/>
/// (<see cref="CallLlmInput.AdditionalInstructions"/>, <see cref="CallLlmInput.AdditionalMessages"/>,
/// <see cref="CallLlmInput.AdditionalToolNames"/>) so that context providers — e.g. an MAF
/// <c>AgentSkillsProvider</c> — stay in effect for every LLM call within a single agent run.
/// </remarks>
internal sealed partial class CallLlmActivity(
    ChatClientRegistry chatClientRegistry,
    ToolRegistry toolRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<CallLlmActivity> logger) : WorkflowActivity<CallLlmInput, CallLlmOutput>
{
    /// <inheritdoc />
    public override async Task<CallLlmOutput> RunAsync(WorkflowActivityContext context, CallLlmInput input)
    {
        AgentTelemetryBaggage.SetAgent(
            input.AgentName,
            input.ChatClientKey,
            AgentTelemetryBaggage.LlmOperation,
            input.TelemetryBaggage);

        var config = chatClientRegistry.Get(input.AgentName);
        if (config is null)
        {
            // Trigger lazy agent resolution — this runs the factory which
            // populates ChatClientRegistry as a side effect for ChatClientAgent types.
            agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
            config = chatClientRegistry.Get(input.AgentName)
                ?? throw new InvalidOperationException(
                    $"Agent '{input.AgentName}' did not register a chat client configuration. " +
                    "Ensure the agent is a ChatClientAgent created via AsAIAgent() or registered " +
                    "with explicit instructions and tools via the DaprAgentsBuilderExtensions overloads.");
        }

        LogLlmCallInfo(input.AgentName, input.Messages.Count);

        try
        {
            var messages = BuildChatMessages(config.Instructions, input.AdditionalInstructions, input.AdditionalMessages, input.Messages);
            var additionalTools = ResolveAdditionalTools(input.AgentName, input.AdditionalToolNames);
            var options = BuildChatOptions(config.Tools, additionalTools, input.Options);

            var response = await config.ChatClient.GetResponseAsync(messages, options)
                .ConfigureAwait(false);

            return ParseResponse(response);
        }
        catch (Exception ex)
        {
            LogLlmCallError(input.AgentName, ex.Message);
            throw;
        }
    }

    private static List<ChatMessage> BuildChatMessages(
        string? instructions,
        string? additionalInstructions,
        List<WorkflowChatMessage>? additionalMessages,
        List<WorkflowChatMessage> messages)
    {
        var chatMessages = new List<ChatMessage>();

        var combinedInstructions = CombineInstructions(instructions, additionalInstructions);
        if (!string.IsNullOrWhiteSpace(combinedInstructions))
        {
            chatMessages.Add(new ChatMessage(ChatRole.System, combinedInstructions));
        }

        // Ephemeral context contributed by AIContextProviders for this run only (e.g. retrieved
        // memories). Never persisted to TurnMessages/session history — see ResolveAgentContextActivity.
        if (additionalMessages is { Count: > 0 })
        {
            foreach (var msg in additionalMessages)
            {
                chatMessages.Add(WorkflowChatMessageConverter.ToChatMessage(msg));
            }
        }

        foreach (var msg in messages)
        {
            chatMessages.Add(WorkflowChatMessageConverter.ToChatMessage(msg));
        }

        return chatMessages;
    }

    private static string? CombineInstructions(string? instructions, string? additionalInstructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return additionalInstructions;
        }

        if (string.IsNullOrWhiteSpace(additionalInstructions))
        {
            return instructions;
        }

        return $"{instructions}\n\n{additionalInstructions}";
    }

    /// <summary>
    /// Resolves tool names contributed by <see cref="ResolveAgentContextActivity"/> (e.g. a skills
    /// provider's <c>load_skill</c>/<c>read_skill_resource</c>/<c>run_skill_script</c>) back into
    /// <see cref="AITool"/> instances via <see cref="ToolRegistry"/>, where they were registered.
    /// </summary>
    private IReadOnlyList<AITool>? ResolveAdditionalTools(string agentName, List<string>? toolNames)
    {
        if (toolNames is not { Count: > 0 })
        {
            return null;
        }

        List<AITool>? resolved = null;
        foreach (var name in toolNames)
        {
            var fn = toolRegistry.Get(agentName, name);
            if (fn is null)
            {
                continue;
            }

            (resolved ??= []).Add(fn);
        }

        return resolved;
    }

    private static ChatOptions? BuildChatOptions(
        IList<AITool>? tools,
        IReadOnlyList<AITool>? additionalTools,
        AgentRunOptions? agentRunOptions)
    {
        var combinedTools = CombineTools(tools, additionalTools);
        if (combinedTools is null && agentRunOptions is null)
        {
            return null;
        }

#pragma warning disable MEAI001
        return new ChatOptions
        {
            AllowBackgroundResponses = agentRunOptions?.AllowBackgroundResponses,
            AdditionalProperties = agentRunOptions?.AdditionalProperties,
            ContinuationToken = agentRunOptions?.ContinuationToken,
            ResponseFormat = agentRunOptions?.ResponseFormat,
            Tools = combinedTools
        };
#pragma warning restore MEAI001
    }

    private static List<AITool>? CombineTools(IList<AITool>? tools, IReadOnlyList<AITool>? additionalTools)
    {
        if (tools is not { Count: > 0 } && additionalTools is not { Count: > 0 })
        {
            return null;
        }

        var combined = new List<AITool>(
            (tools?.Count ?? 0) + (additionalTools?.Count ?? 0));

        if (tools is { Count: > 0 })
        {
            combined.AddRange(tools);
        }

        if (additionalTools is { Count: > 0 })
        {
            combined.AddRange(additionalTools);
        }

        return combined;
    }

    private static CallLlmOutput ParseResponse(ChatResponse response)
    {
        var responseMessage = response.Messages[^1];
        var functionCalls = responseMessage.Contents.OfType<FunctionCallContent>().ToList();
        var text = responseMessage.Text;

        var isFinal = functionCalls.Count == 0;

        return new CallLlmOutput
        {
            IsFinal = isFinal,
            Text = text,
            FunctionCalls = isFinal
                ? null
                : functionCalls.Select(fc => new WorkflowFunctionCall
                {
                    CallId = fc.CallId ?? Guid.NewGuid().ToString("N"),
                    Name = fc.Name,
                    ArgumentsJson = fc.Arguments is { Count: > 0 }
                        ? JsonSerializer.Serialize(fc.Arguments)
                        : "{}"
                }).ToList()
        };
    }

    [LoggerMessage(LogLevel.Information, "Calling LLM for agent '{AgentName}' with {MessageCount} messages")]
    private partial void LogLlmCallInfo(string agentName, int messageCount);

    [LoggerMessage(LogLevel.Error, "LLM call failed for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogLlmCallError(string agentName, string errorMessage);
}
