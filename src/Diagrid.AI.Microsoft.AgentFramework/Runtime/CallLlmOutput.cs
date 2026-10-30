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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Output payload from <see cref="CallLlmActivity"/>. Indicates whether the LLM returned
/// a final text response or requested tool calls.
/// </summary>
internal sealed record CallLlmOutput
{
    /// <summary>Whether this is a final response (no pending tool calls).</summary>
    public bool IsFinal { get; init; }

    /// <summary>Text content from the LLM (may be present even when tool calls are requested).</summary>
    public string? Text { get; init; }

    /// <summary>Error message if the LLM call failed.</summary>
    public string? Error { get; init; }

    /// <summary>Tool (function) calls requested by the LLM. Null or empty when <see cref="IsFinal"/> is true.</summary>
    public List<WorkflowFunctionCall>? FunctionCalls { get; init; }

    /// <summary>Reasoning the model returned with this response, in order, or null if none.</summary>
    public List<WorkflowReasoningContent>? Reasoning { get; init; }

    /// <summary>
    /// The agent's reasoning replay setting, resolved when the call ran. Recorded in the output so
    /// the workflow decides whether to keep <see cref="Reasoning"/> the same way on every replay.
    /// <see langword="null"/> in outputs recorded before this existed, which means
    /// <see cref="Abstractions.ReasoningReplay.AcrossTurns"/>.
    /// </summary>
    public Abstractions.ReasoningReplay? ReasoningReplay { get; init; }

    /// <summary>
    /// How many stored conversation entries the call loaded, or <see langword="null"/> without a conversation.
    /// The run pins its history to the value its first call recorded.
    /// </summary>
    public long? HistorySequence { get; init; }

    /// <summary>
    /// The incarnation of the stored conversation the call loaded, or <see langword="null"/> when it was empty
    /// or there was none.
    /// </summary>
    public string? HistoryIncarnation { get; init; }

    /// <summary>
    /// <see langword="true"/> when the stored history the call loaded has grown past the agent's compaction
    /// trigger (<c>WithConversationCompaction</c>), so the run compacts the conversation after its commit.
    /// <see langword="null"/> otherwise.
    /// </summary>
    public bool? CompactionDue { get; init; }
}
