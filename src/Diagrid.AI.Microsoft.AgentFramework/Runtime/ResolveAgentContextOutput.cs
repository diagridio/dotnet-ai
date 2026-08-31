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
/// Output payload from <see cref="ResolveAgentContextActivity"/>: the merged contribution of every
/// <c>AIContextProvider</c> configured for the agent, ready to be threaded through every
/// <see cref="CallLlmInput"/> for the remainder of the run.
/// </summary>
internal sealed record ResolveAgentContextOutput
{
    /// <summary>Additional system instructions to append after the agent's own instructions.</summary>
    public string? Instructions { get; init; }

    /// <summary>Ephemeral context messages to include in every LLM call for this run only.</summary>
    public List<WorkflowChatMessage>? Messages { get; init; }

    /// <summary>Names of additional tools (already registered into <see cref="ToolRegistry"/>) to make available for this run.</summary>
    public List<string>? ToolNames { get; init; }

    /// <summary>
    /// The <c>AgentSession</c> used while resolving context — serialized (via
    /// <c>AIAgent.SerializeSessionAsync</c>) after every provider's <c>InvokingAsync</c> has run, so
    /// any state a provider wrote to <c>Session.StateBag</c> is captured. Threaded through
    /// <see cref="AgentRunWorkflow"/> into <see cref="CompleteAgentContextInput"/> so
    /// <see cref="CompleteAgentContextActivity"/> can reconstruct the same logical session for
    /// <c>InvokedAsync</c> — each activity gets its own deserialized instance (durability requires
    /// this to be data, not a shared live object), but it carries the same state. <c>null</c> when
    /// there were no context providers to resolve.
    /// </summary>
    public string? SerializedSessionJson { get; init; }
}
