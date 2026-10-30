// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Mock <see cref="IChatClient"/> for an agent with conversation compaction. A summary request (its system
/// message is the default summarization prompt) is answered with <see cref="Summary"/>; any other request is
/// answered with "reply to: " and the last user message. Every request's message texts are recorded by the
/// conversation id the test puts in its user messages.
/// </summary>
internal sealed class CompactionMockChatClient : IChatClient
{
    internal const string AgentName = "CompactingAgent";
    internal const string Summary = "SUMMARY-OF-EARLIER-TURNS";

    internal static ConcurrentQueue<List<string>> Requests { get; } = new();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        Requests.Enqueue(history.Select(m => $"{m.Role.Value}: {m.Text}").ToList());

        var text = history.FirstOrDefault()?.Text == ConversationCompactionOptions.DefaultSummarizationPrompt
            ? Summary
            : "reply to: " + history.Last(m => m.Role == ChatRole.User).Text;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
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
