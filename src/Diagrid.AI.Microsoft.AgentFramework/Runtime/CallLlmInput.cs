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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Input payload for <see cref="CallLlmActivity"/>. Contains the agent name and the
/// conversation history needed for a single LLM turn.
/// </summary>
internal sealed record CallLlmInput(
    string AgentName,
    string? ChatClientKey,
    List<WorkflowChatMessage> Messages,
    AgentRunOptions? Options = null,
    Dictionary<string, string?>? TelemetryBaggage = null)
{
    /// <summary>
    /// Additional system instructions contributed by the agent's <c>AIContextProviders</c>
    /// (e.g. an MAF <c>AgentSkillsProvider</c>'s skills catalog), computed once per run by
    /// <see cref="ResolveAgentContextActivity"/> and appended after the agent's own instructions
    /// on every LLM call within that run.
    /// </summary>
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// Ephemeral context messages contributed by the agent's <c>AIContextProviders</c> for this
    /// run only. Included in the LLM call but never persisted to <c>TurnMessages</c>/session
    /// history — see <see cref="ResolveAgentContextActivity"/>.
    /// </summary>
    public List<WorkflowChatMessage>? AdditionalMessages { get; init; }

    /// <summary>
    /// Names of additional tools contributed by the agent's <c>AIContextProviders</c> (e.g. a
    /// skills provider's <c>load_skill</c>/<c>read_skill_resource</c>/<c>run_skill_script</c>),
    /// resolved from <see cref="ToolRegistry"/> and merged into <c>ChatOptions.Tools</c> for every
    /// LLM call within the run.
    /// </summary>
    public List<string>? AdditionalToolNames { get; init; }

    /// <summary>
    /// Index in <see cref="Messages"/> of the first message of the current run; the ones before it
    /// are history from earlier turns. <see cref="Abstractions.ReasoningReplay.WithinRun"/> sends
    /// back only the reasoning from this index on. <see langword="null"/> (inputs recorded before this
    /// existed) treats every message as part of the run.
    /// </summary>
    public int? RunMessagesStart { get; init; }

    /// <summary>
    /// Zero-based index of this LLM call within the run. From the second call on, a required tool
    /// mode is not sent again, as <c>FunctionInvokingChatClient</c> does, or a model that obeys it
    /// would call tools until the run hits its iteration limit. <see langword="null"/> (inputs
    /// recorded before this existed) counts as the first call.
    /// </summary>
    public int? Iteration { get; init; }

    /// <summary>
    /// The stored conversation whose history goes before <see cref="Messages"/>, or <see langword="null"/>.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// The history prefix to load: entries below this sequence. <see langword="null"/> on the run's first call,
    /// which loads the whole conversation and records how much it loaded in
    /// <see cref="CallLlmOutput.HistorySequence"/>; later calls pass that back, so every call of a run sees the
    /// same history even while other runs append to it.
    /// </summary>
    public long? HistoryUpToSequence { get; init; }

    /// <summary>
    /// The conversation incarnation the first call loaded, so a later call fails rather than loading a
    /// conversation created again under the same id.
    /// </summary>
    public string? HistoryIncarnation { get; init; }
}
