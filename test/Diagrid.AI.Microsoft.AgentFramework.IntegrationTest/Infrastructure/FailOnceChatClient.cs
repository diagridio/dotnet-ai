// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Wraps <see cref="ReasoningMockChatClient"/> and fails the second LLM call of a run (the one after the tool
/// result) once per distinct user message that contains <see cref="Marker"/>. A test can then check that a
/// run that fails part-way stores nothing, and that its retry stores exactly one turn.
/// </summary>
internal sealed class FailOnceChatClient() : DelegatingChatClient(new ReasoningMockChatClient())
{
    internal const string AgentName = "FlakyReasoningAgent";
    internal const string Marker = "fail-once";

    private static readonly ConcurrentDictionary<string, bool> Failed = new(StringComparer.Ordinal);

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var userText = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        if (history[^1].Role != ChatRole.User && userText.Contains(Marker, StringComparison.Ordinal)
            && Failed.TryAdd(userText, true))
        {
            throw new InvalidOperationException("Injected LLM failure after the tool call.");
        }

        return base.GetResponseAsync(history, options, cancellationToken);
    }
}
