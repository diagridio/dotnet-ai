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
    /// <param name="message">The message to convert.</param>
    /// <param name="reasoningSource">
    /// The source (see <see cref="GetReasoningSource"/>) of the LLM call the message is for. Only
    /// reasoning blocks produced by that same source are included, because a provider rejects
    /// another provider's signed or encrypted reasoning. <see langword="null"/> includes none, which
    /// is what the <see cref="AIContextProvider"/> pipeline uses, so providers see what they saw
    /// before reasoning was kept.
    /// </param>
    public static ChatMessage ToChatMessage(WorkflowChatMessage message, string? reasoningSource = null)
    {
        var role = message.Role switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        var contents = new List<AIContent>();

        // Reasoning goes first: providers such as Anthropic require thinking blocks before tool calls.
        if (reasoningSource is not null && message.Reasoning is { Count: > 0 })
        {
            foreach (var reasoning in message.Reasoning)
            {
                if (string.Equals(reasoning.Source, reasoningSource, StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(new TextReasoningContent(reasoning.Text) { ProtectedData = reasoning.ProtectedData });
                }
            }
        }

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
    /// Converts a <see cref="WorkflowChatMessage"/> into the <see cref="ChatMessage"/> an
    /// <see cref="AIContextProvider"/> sees, without the message's reasoning.
    /// </summary>
    public static ChatMessage ToProviderMessage(WorkflowChatMessage message) =>
        ToChatMessage(message, reasoningSource: null);

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
        // Reasoning on a provider-contributed message has no known source, so it is kept but never sent back.
        var reasoning = ToWorkflowReasoning(message.Contents, source: null);

        return new WorkflowChatMessage
        {
            Role = role,
            Content = string.IsNullOrEmpty(text) ? null : text,
            FunctionCalls = functionCalls.Count > 0 ? functionCalls : null,
            FunctionResults = functionResults.Count > 0 ? functionResults : null,
            Reasoning = reasoning
        };
    }

    /// <summary>
    /// The source recorded on reasoning an agent's LLM call produced: the agent name, plus the chat
    /// client key when there is one.
    /// </summary>
    public static string GetReasoningSource(string agentName, string? chatClientKey) =>
        string.IsNullOrEmpty(chatClientKey) ? agentName : $"{agentName}/{chatClientKey}";

    /// <summary>
    /// Collects the <see cref="TextReasoningContent"/> blocks in <paramref name="contents"/>, in order,
    /// keeping those with text or <see cref="TextReasoningContent.ProtectedData"/>.
    /// </summary>
    /// <param name="contents">The response contents.</param>
    /// <param name="source">
    /// The source to record on blocks that can be sent back faithfully; see <see cref="CanReplay"/>.
    /// </param>
    /// <returns>The reasoning blocks, or <see langword="null"/> when there are none.</returns>
    public static List<WorkflowReasoningContent>? ToWorkflowReasoning(IEnumerable<AIContent> contents, string? source)
    {
        List<WorkflowReasoningContent>? reasoning = null;
        foreach (var content in contents.OfType<TextReasoningContent>())
        {
            if (string.IsNullOrEmpty(content.Text) && content.ProtectedData is null)
            {
                continue;
            }

            (reasoning ??= []).Add(new WorkflowReasoningContent
            {
                Text = string.IsNullOrEmpty(content.Text) ? null : content.Text,
                ProtectedData = content.ProtectedData,
                Source = source is not null && CanReplay(content) ? source : null
            });
        }

        return reasoning;
    }

    /// <summary>
    /// Whether a reasoning block can be rebuilt from its text and protected data alone and sent back.
    /// </summary>
    /// <remarks>
    /// Only the text and <see cref="TextReasoningContent.ProtectedData"/> survive the workflow, not the
    /// provider's own object (<see cref="AIContent.RawRepresentation"/>). Two kinds of block need more:
    /// a block with no text (Anthropic <c>redacted_thinking</c>, OpenAI encrypted reasoning without a
    /// summary) would come back as an ordinary thinking block, and an OpenAI Responses reasoning item
    /// would come back without its <c>rs_…</c> id. Providers reject both, so those blocks are kept for
    /// the caller but not sent back. The OpenAI type is matched by name, so this library does not
    /// depend on the OpenAI package.
    /// </remarks>
    private static bool CanReplay(TextReasoningContent content) =>
        !string.IsNullOrEmpty(content.Text) && !IsOpenAIResponsesReasoningItem(content.RawRepresentation);

    private const string OpenAIResponsesReasoningItemType = "OpenAI.Responses.ReasoningResponseItem";

    private static bool IsOpenAIResponsesReasoningItem(object? rawRepresentation)
    {
        for (var type = rawRepresentation?.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == OpenAIResponsesReasoningItemType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates the assistant <see cref="ChatMessage"/> returned to callers: the reasoning blocks, then the text.
    /// <see cref="ChatMessage.Text"/> concatenates only text content, so it is the same with or without reasoning.
    /// </summary>
    public static ChatMessage CreateAssistantMessage(string? text, List<WorkflowReasoningContent>? reasoning)
    {
        if (reasoning is not { Count: > 0 })
        {
            return new ChatMessage(ChatRole.Assistant, text ?? string.Empty);
        }

        var contents = new List<AIContent>(reasoning.Count + 1);
        foreach (var block in reasoning)
        {
            contents.Add(new TextReasoningContent(block.Text) { ProtectedData = block.ProtectedData });
        }

        contents.Add(new TextContent(text ?? string.Empty));
        return new ChatMessage(ChatRole.Assistant, contents);
    }
}
