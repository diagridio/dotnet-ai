// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Collections.Concurrent;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

public sealed class TelemetryBaggageRecorder
{
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _llmBaggage = new();
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _toolBaggage = new();

    public IReadOnlyCollection<IReadOnlyDictionary<string, string>> LlmBaggage => _llmBaggage.ToArray();

    public IReadOnlyCollection<IReadOnlyDictionary<string, string>> ToolBaggage => _toolBaggage.ToArray();

    public void RecordLlm(IReadOnlyDictionary<string, string> baggage) => _llmBaggage.Enqueue(baggage);

    public void RecordTool(IReadOnlyDictionary<string, string> baggage) => _toolBaggage.Enqueue(baggage);

    public void Reset()
    {
        _llmBaggage.Clear();
        _toolBaggage.Clear();
    }
}
