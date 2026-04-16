// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Stateless mock <see cref="IChatClient"/> that simulates a two-step LLM interaction for
/// testing <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentContextAccessor"/>:
/// <list type="number">
///   <item>If the conversation history has <b>no</b> <see cref="FunctionResultContent"/>, requests
///         the <c>get_context</c> tool.</item>
///   <item>If the history already contains a <see cref="FunctionResultContent"/>, returns the tool
///         result as the final text response.</item>
/// </list>
/// The registered <c>get_context</c> tool reads the ambient context during
/// <c>ExecuteToolActivity</c> and returns <c>"instanceId:{id}"</c>.
/// </summary>
internal sealed class ContextMockChatClient : IChatClient
{
    internal const string ToolName = "get_context";
    internal const string ToolCallId = "ctx-call-1";

    public ChatClientMetadata Metadata { get; } = new ChatClientMetadata("mock-context-client");

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
            // First turn: request the get_context tool.
            response = new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent(ToolCallId, ToolName, new Dictionary<string, object?>())]));
        }
        else
        {
            // Second turn: return the tool result as the final text response.
            var toolResult = messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .Select(r => r.Result?.ToString())
                .FirstOrDefault() ?? "null";

            response = new ChatResponse(new ChatMessage(ChatRole.Assistant, toolResult));
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
