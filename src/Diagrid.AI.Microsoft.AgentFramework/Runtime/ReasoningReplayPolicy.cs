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

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// The rules of <see cref="ReasoningReplay"/> in one place: which reasoning an LLM call sends back
/// (<see cref="CallLlmActivity"/>) and which reasoning the run's messages keep (<see cref="AgentRunWorkflow"/>).
/// Pure, so the workflow can ask it on every replay and get the same answer.
/// </summary>
internal static class ReasoningReplayPolicy
{
    /// <summary>
    /// Whether the reasoning on the message at <paramref name="index"/> goes back to the model. Reasoning this agent
    /// produced earlier goes back from prior turns too with <see cref="ReasoningReplay.AcrossTurns"/>, from this run
    /// only with <see cref="ReasoningReplay.WithinRun"/>, and never with <see cref="ReasoningReplay.Off"/>.
    /// </summary>
    /// <param name="replay">The agent's replay setting.</param>
    /// <param name="index">The message's position among the call's conversation messages.</param>
    /// <param name="runMessagesStart">Where the run's own messages start; the ones before it are earlier turns.</param>
    internal static bool SendsReasoning(ReasoningReplay replay, int index, int runMessagesStart) =>
        replay switch
        {
            ReasoningReplay.Off => false,
            ReasoningReplay.WithinRun => index >= runMessagesStart,
            _ => true
        };

    /// <summary>
    /// Whether reasoning outlives the run: kept on the earlier turns the run carries, and in the
    /// <see cref="AgentRunResult.TurnMessages"/> later turns are given. Not with <see cref="ReasoningReplay.WithinRun"/>
    /// or <see cref="ReasoningReplay.Off"/>, where it is never sent back from an earlier turn, so history does not grow
    /// with it. An unknown setting (<see langword="null"/>) keeps it.
    /// </summary>
    /// <param name="replay">The replay setting an LLM call recorded, or <see langword="null"/>.</param>
    internal static bool KeepsReasoningAcrossTurns(ReasoningReplay? replay) =>
        replay is not (ReasoningReplay.WithinRun or ReasoningReplay.Off);

    /// <summary>
    /// Whether an assistant message of the run keeps the reasoning that came with it: not with
    /// <see cref="ReasoningReplay.Off"/>, so its messages do not grow with reasoning never sent back.
    /// </summary>
    /// <param name="replay">The replay setting the LLM call recorded, or <see langword="null"/>.</param>
    internal static bool KeepsReasoningWithinRun(ReasoningReplay? replay) =>
        replay != ReasoningReplay.Off;
}
