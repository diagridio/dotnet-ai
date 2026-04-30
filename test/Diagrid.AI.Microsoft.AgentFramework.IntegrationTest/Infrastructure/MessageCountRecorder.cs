// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Thread-safe recorder of per-call message counts used by integration tests to verify that
/// <c>HistoryAgent</c> receives conversation history from prior session turns.
/// </summary>
public sealed class MessageCountRecorder
{
    private readonly object _lock = new();
    private readonly List<int> _counts = [];

    /// <summary>Records the number of messages seen in a single LLM call.</summary>
    public void Record(int count)
    {
        lock (_lock) _counts.Add(count);
    }

    /// <summary>Returns a snapshot of all recorded message counts in call order.</summary>
    public IReadOnlyList<int> Counts
    {
        get { lock (_lock) return [.._counts]; }
    }

    /// <summary>Clears all recorded counts.</summary>
    public void Reset()
    {
        lock (_lock) _counts.Clear();
    }
}
