// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

internal sealed class TelemetryBaggageMockChatClient(TelemetryBaggageRecorder recorder) : IChatClient
{
    internal const string AgentName = "TelemetryBaggageAgent";
    internal const string ToolName = "record_baggage";
    internal const string ToolCallId = "telemetry-call-1";

    public ChatClientMetadata Metadata { get; } = new ChatClientMetadata("mock-telemetry-baggage-client");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordLlm(GetCurrentBaggage());

        var hasToolResult = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Any();

        var response = hasToolResult
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant, "telemetry complete"))
            : new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent(ToolCallId, ToolName)]));

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

    internal static IReadOnlyDictionary<string, string> GetCurrentBaggage() =>
        Activity.Current?.Baggage.ToDictionary(
            item => item.Key,
            item => item.Value ?? string.Empty,
            StringComparer.Ordinal) ?? new Dictionary<string, string>();
}
