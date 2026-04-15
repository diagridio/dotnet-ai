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
/// Activity that performs a single LLM call. Each invocation is checkpointed by Dapr Workflows,
/// so on crash recovery the result is replayed without re-executing the LLM call.
/// </summary>
internal sealed partial class CallLlmActivity(
    ChatClientRegistry chatClientRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<CallLlmActivity> logger) : WorkflowActivity<CallLlmInput, CallLlmOutput>
{
    /// <inheritdoc />
    public override async Task<CallLlmOutput> RunAsync(WorkflowActivityContext context, CallLlmInput input)
    {
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
            var messages = BuildChatMessages(config.Instructions, input.Messages);
            var options = BuildChatOptions(config.Tools);

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

    private static List<ChatMessage> BuildChatMessages(string? instructions, List<WorkflowChatMessage> messages)
    {
        var chatMessages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            chatMessages.Add(new ChatMessage(ChatRole.System, instructions));
        }

        foreach (var msg in messages)
        {
            chatMessages.Add(ConvertToChatMessage(msg));
        }

        return chatMessages;
    }

    private static ChatOptions? BuildChatOptions(IList<AITool>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return null;
        }

        return new ChatOptions { Tools = [.. tools] };
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

    private static ChatMessage ConvertToChatMessage(WorkflowChatMessage msg)
    {
        var role = msg.Role switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        var contents = new List<AIContent>();

        if (msg.Content is not null)
        {
            contents.Add(new TextContent(msg.Content));
        }

        if (msg.FunctionCalls is { Count: > 0 })
        {
            foreach (var fc in msg.FunctionCalls)
            {
                var args = string.IsNullOrEmpty(fc.ArgumentsJson) || fc.ArgumentsJson == "{}"
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.ArgumentsJson);
                contents.Add(new FunctionCallContent(fc.CallId, fc.Name, args));
            }
        }

        if (msg.FunctionResults is { Count: > 0 })
        {
            foreach (var fr in msg.FunctionResults)
            {
                object? result = fr.ResultJson is not null
                    ? JsonSerializer.Deserialize<JsonElement>(fr.ResultJson)
                    : null;
                contents.Add(new FunctionResultContent(fr.CallId, result));
            }
        }

        return new ChatMessage(role, contents);
    }

    [LoggerMessage(LogLevel.Information, "Calling LLM for agent '{AgentName}' with {MessageCount} messages")]
    private partial void LogLlmCallInfo(string agentName, int messageCount);

    [LoggerMessage(LogLevel.Error, "LLM call failed for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogLlmCallError(string agentName, string errorMessage);
}
