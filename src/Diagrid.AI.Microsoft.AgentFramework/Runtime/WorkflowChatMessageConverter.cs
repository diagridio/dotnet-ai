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
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Converts between the JSON-serializable <see cref="WorkflowChatMessage"/> (safe to pass through
/// Dapr Workflow orchestrator/activity boundaries) and <see cref="ChatMessage"/> (used by
/// <see cref="IChatClient"/> and <see cref="AIContextProvider"/>). Shared by
/// <see cref="CallLlmActivity"/>, <see cref="ResolveAgentContextActivity"/>, and
/// <see cref="CompleteAgentContextActivity"/> so the two representations stay in sync.
/// </summary>
internal static class WorkflowChatMessageConverter
{
    /// <summary>
    /// Converts a <see cref="WorkflowChatMessage"/> into a <see cref="ChatMessage"/> suitable for
    /// sending to an <see cref="IChatClient"/>.
    /// </summary>
    public static ChatMessage ToChatMessage(WorkflowChatMessage message)
    {
        var role = message.Role switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        var contents = new List<AIContent>();

        if (message.Content is not null)
        {
            contents.Add(new TextContent(message.Content));
        }

        if (message.FunctionCalls is { Count: > 0 })
        {
            foreach (var fc in message.FunctionCalls)
            {
                var args = string.IsNullOrEmpty(fc.ArgumentsJson) || fc.ArgumentsJson == "{}"
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.ArgumentsJson);
                contents.Add(new FunctionCallContent(fc.CallId, fc.Name, args));
            }
        }

        if (message.FunctionResults is { Count: > 0 })
        {
            foreach (var fr in message.FunctionResults)
            {
                object? result = fr.ResultJson is not null
                    ? JsonSerializer.Deserialize<JsonElement>(fr.ResultJson)
                    : null;
                contents.Add(new FunctionResultContent(fr.CallId, result));
            }
        }

        return new ChatMessage(role, contents);
    }

    /// <summary>
    /// Converts a <see cref="ChatMessage"/> — typically one contributed by an
    /// <see cref="AIContextProvider"/> via <c>AIContext.Messages</c> — into a
    /// JSON-serializable <see cref="WorkflowChatMessage"/> so it can flow through the workflow
    /// orchestrator and back into subsequent activity inputs.
    /// </summary>
    public static WorkflowChatMessage FromChatMessage(ChatMessage message)
    {
        var role = message.Role == ChatRole.System ? "system"
            : message.Role == ChatRole.Assistant ? "assistant"
            : message.Role == ChatRole.Tool ? "tool"
            : "user";

        var functionCalls = message.Contents.OfType<FunctionCallContent>()
            .Select(fc => new WorkflowFunctionCall
            {
                CallId = fc.CallId,
                Name = fc.Name,
                ArgumentsJson = fc.Arguments is { Count: > 0 } ? JsonSerializer.Serialize(fc.Arguments) : "{}"
            })
            .ToList();

        var functionResults = message.Contents.OfType<FunctionResultContent>()
            .Select(fr => new WorkflowFunctionResult
            {
                CallId = fr.CallId,
                Name = string.Empty, // FunctionResultContent does not carry the originating function name.
                ResultJson = fr.Result is not null ? JsonSerializer.Serialize(fr.Result) : null
            })
            .ToList();

        var text = message.Text;

        return new WorkflowChatMessage
        {
            Role = role,
            Content = string.IsNullOrEmpty(text) ? null : text,
            FunctionCalls = functionCalls.Count > 0 ? functionCalls : null,
            FunctionResults = functionResults.Count > 0 ? functionResults : null
        };
    }
}
