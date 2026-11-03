// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Stateless mock <see cref="IChatClient"/> for a reasoning model. Every response carries a
/// <see cref="TextReasoningContent"/> numbered after the reasoning blocks it received
/// (<c>thought-1</c>, <c>thought-2</c>, ...), so a test can tell which earlier reasoning reached
/// the model. When the last message is the user's, it calls <see cref="ToolName"/>; once the tool
/// result is in, it answers with <c>seen:</c> followed by the reasoning it received.
/// </summary>
internal sealed class ReasoningMockChatClient : IChatClient
{
    internal const string AgentName = "ReasoningAgent";
    internal const string ToolName = "note";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var received = history
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Select(r => r.Text)
            .ToList();
        var reasoning = new TextReasoningContent($"thought-{received.Count + 1}")
        {
            ProtectedData = $"sig-{received.Count + 1}"
        };

        AIContent reply = history[^1].Role == ChatRole.User
            ? new FunctionCallContent($"call-{received.Count + 1}", ToolName)
            : new TextContent("seen:" + string.Join(",", received));

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [reasoning, reply])));
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
