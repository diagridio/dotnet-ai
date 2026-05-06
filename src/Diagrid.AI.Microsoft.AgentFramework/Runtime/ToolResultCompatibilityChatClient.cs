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

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// An <see cref="IChatClient"/> decorator that rewrites tool-result messages before forwarding
/// them to an inner client that cannot natively handle <see cref="FunctionResultContent"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Dapr.AI.Microsoft.Extensions.DaprChatClient</c> (≤ 1.17.x) maps
/// <see cref="FunctionCallContent"/> on assistant messages to OpenAI <c>tool_calls</c>, but
/// contains no code for <see cref="FunctionResultContent"/>. When the second LLM call carries
/// a <c>tool</c>-role message, <c>DaprChatClient</c> either drops the content or sends a
/// <c>ToolMessage</c> with an empty <c>Id</c>, causing OpenAI to return HTTP 400
/// "messages with role 'tool' must be a response to a preceding message with 'tool_calls'".
/// </para>
/// <para>
/// This adapter converts each <see cref="FunctionResultContent"/> item to a
/// <see cref="TextContent"/> while preserving the <see cref="ChatRole.Tool"/> role and
/// storing the call identifier in
/// <c>AdditionalProperties["tool_call_id"]</c> so that well-behaved downstream adapters
/// can still forward the identifier to the provider.
/// </para>
/// </remarks>
internal sealed class ToolResultCompatibilityChatClient(IChatClient inner) : IChatClient
{
    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rewritten = RewriteMessages(messages);
        return inner.GetResponseAsync(rewritten, options, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rewritten = RewriteMessages(messages);
        await foreach (var update in inner.GetStreamingResponseAsync(rewritten, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ToolResultCompatibilityChatClient)
            ? this
            : inner.GetService(serviceType, serviceKey);

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();

    private static List<ChatMessage> RewriteMessages(IEnumerable<ChatMessage> messages) => messages
        .Select(msg => ContainsFunctionResult(msg) ? RewriteFunctionResults(msg) : msg).ToList();

    private static bool ContainsFunctionResult(ChatMessage msg) => msg.Contents.OfType<FunctionResultContent>().Any();

    /// <summary>
    /// Replaces each <see cref="FunctionResultContent"/> in <paramref name="original"/> with a
    /// <see cref="TextContent"/> whose <c>AdditionalProperties["tool_call_id"]</c> carries the
    /// original call identifier.
    /// All other content items and message metadata are passed through unchanged.
    /// </summary>
    internal static ChatMessage RewriteFunctionResults(ChatMessage original)
    {
        var newContents = new List<AIContent>(original.Contents.Count);

        foreach (var content in original.Contents)
        {
            if (content is FunctionResultContent frc)
            {
                var resultText = frc.Result is JsonElement je
                    ? je.GetRawText()
                    : JsonSerializer.Serialize(frc.Result);

                var textContent = new TextContent(resultText);
                (textContent.AdditionalProperties ??= [])[ToolCallIdKey] = frc.CallId;
                newContents.Add(textContent);
            }
            else
            {
                newContents.Add(content);
            }
        }

        var newMsg = new ChatMessage(original.Role, newContents);
        if (original.AuthorName is not null)
            newMsg.AuthorName = original.AuthorName;

        if (original.AdditionalProperties is { Count: > 0 })
        {
            newMsg.AdditionalProperties ??= [];
            foreach (var kvp in original.AdditionalProperties)
                newMsg.AdditionalProperties[kvp.Key] = kvp.Value;
        }

        return newMsg;
    }

    /// <summary>The key used to store the tool call identifier in <see cref="AIContent.AdditionalProperties"/>.</summary>
    internal const string ToolCallIdKey = "tool_call_id";
}
