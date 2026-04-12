// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Thread-safe counter used by integration tests to verify that the
/// <c>process_input</c> tool was invoked by <c>ToolInvocationAgent</c>.
/// </summary>
public sealed class ToolInvocationTracker
{
    private int _count;

    /// <summary>Increments the invocation count by one.</summary>
    public void RecordInvocation() => Interlocked.Increment(ref _count);

    /// <summary>Returns the total number of tool invocations recorded so far.</summary>
    public int InvocationCount => Volatile.Read(ref _count);

    /// <summary>Resets the invocation count to zero.</summary>
    public void Reset() => Volatile.Write(ref _count, 0);
}
