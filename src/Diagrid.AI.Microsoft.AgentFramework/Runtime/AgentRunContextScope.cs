// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Establishes <see cref="AIAgent.CurrentRunContext"/> for the duration of a context-provider
/// invocation — the ambient ("ambient" in the AsyncLocal sense) ledger MAF's own
/// <c>AIAgent.RunAsync</c> normally sets up before delegating to <c>RunCoreAsync</c>, exposing the
/// session, request messages, and <see cref="AgentRunOptions"/> (including
/// <see cref="AgentRunOptions.AdditionalProperties"/>) to any provider that checks it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AIAgent.CurrentRunContext"/>'s setter is <c>protected</c>, reachable only from within
/// <see cref="AIAgent"/> or a type deriving from it — this class exists purely to reach it, since
/// our Dapr Workflow activities call <c>AIContextProvider.InvokingAsync</c>/<c>InvokedAsync</c>
/// directly and intentionally never go through <c>AIAgent.RunAsync</c> itself (see
/// <see cref="ResolveAgentContextActivity"/>/<see cref="CompleteAgentContextActivity"/>). It is never
/// instantiated — only its static <see cref="Enter"/> method is used, exactly mirroring what
/// <c>AIAgent.RunAsync</c> does internally.
/// </para>
/// <para>
/// Safe across concurrently-running activities: <see cref="AIAgent.CurrentRunContext"/> is backed by
/// <see cref="System.Threading.AsyncLocal{T}"/>, so each activity invocation's own async flow sees
/// only what it (or an ancestor in its own call chain) set — the same guarantee
/// <c>DaprAgentContextAccessor</c> already relies on elsewhere in this codebase. It is NOT a durable
/// carrier — never rely on a value set in one workflow activity being visible in another; each
/// activity that needs it must call <see cref="Enter"/> for itself from data threaded through its
/// own (serializable) input.
/// </para>
/// </remarks>
internal abstract class AgentRunContextScope : AIAgent
{
    /// <summary>
    /// Sets <see cref="AIAgent.CurrentRunContext"/> for the current async flow and returns an
    /// <see cref="IDisposable"/> that restores the previous value (typically <c>null</c>) when
    /// disposed — use with <c>using</c> to scope it to exactly the provider call(s) that need it.
    /// </summary>
    /// <param name="agent">The agent the run belongs to.</param>
    /// <param name="session">The session for this run.</param>
    /// <param name="requestMessages">The new message(s) for this run.</param>
    /// <param name="runOptions">
    /// The run's options (e.g. <see cref="AgentRunOptions.AdditionalProperties"/>) — defaults to an
    /// empty <see cref="AgentRunOptions"/> when <c>null</c>, since <c>AgentRunContext</c> requires one.
    /// </param>
    public static IDisposable Enter(
        AIAgent agent,
        AgentSession session,
        IReadOnlyCollection<ChatMessage> requestMessages,
        AgentRunOptions? runOptions)
    {
        var previous = CurrentRunContext;
        CurrentRunContext = new AgentRunContext(agent, session, requestMessages, runOptions ?? new AgentRunOptions());
        return new Restorer(previous);
    }

    private sealed class Restorer(AgentRunContext? previous) : IDisposable
    {
        public void Dispose() => CurrentRunContext = previous!;
    }
}
