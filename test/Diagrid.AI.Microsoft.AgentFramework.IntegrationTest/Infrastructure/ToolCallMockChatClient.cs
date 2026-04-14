// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Stateless mock <see cref="IChatClient"/> that simulates a two-step LLM interaction:
/// <list type="number">
///   <item>If the conversation history has <b>no</b> <see cref="FunctionResultContent"/>, requests
///         the <c>process_input</c> tool with <c>input = "test-value"</c>.</item>
///   <item>If the history already contains a <see cref="FunctionResultContent"/>, returns a text
///         response embedding the tool result.</item>
/// </list>
/// The decision is made purely from the message history, making this safe to use with a singleton
/// <c>ChatClientAgent</c> across multiple test invocations.
/// </summary>
internal sealed class ToolCallMockChatClient : IChatClient
{
    internal const string ToolName       = "process_input";
    internal const string ToolCallId     = "call-1";
    internal const string ToolInputValue = "test-value";

    public ChatClientMetadata Metadata { get; } = new ChatClientMetadata("mock-tool-client");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var hasToolResult = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Any();

        ChatResponse response;
        if (!hasToolResult)
        {
            // First turn: ask the LLM to invoke the tool.
            response = new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent(
                    ToolCallId,
                    ToolName,
                    new Dictionary<string, object?> { ["input"] = ToolInputValue })]));
        }
        else
        {
            // Second turn: incorporate the tool result into the final answer.
            var toolResult = messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .Select(r => r.Result?.ToString())
                .FirstOrDefault() ?? "no-result";

            response = new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                $"Tool returned: {toolResult}"));
        }

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
