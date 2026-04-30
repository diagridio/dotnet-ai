// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Mock <see cref="IChatClient"/> that records the number of messages it receives on each call
/// into a <see cref="MessageCountRecorder"/>. Used by <c>HistoryAgent</c> to verify that
/// conversation history from prior session turns is injected into subsequent LLM calls.
/// </summary>
internal sealed class MessageCountMockChatClient(MessageCountRecorder recorder) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new ChatClientMetadata("mock-message-count-client");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(messages.Count());
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "history-agent-response"));
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
