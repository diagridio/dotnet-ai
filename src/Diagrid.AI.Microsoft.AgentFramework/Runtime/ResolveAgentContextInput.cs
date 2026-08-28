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
/// Input payload for <see cref="ResolveAgentContextActivity"/>.
/// </summary>
internal sealed record ResolveAgentContextInput(
    string AgentName,
    string? ChatClientKey,
    Dictionary<string, string?>? TelemetryBaggage = null)
{
    /// <summary>
    /// The new message(s) for this run (not prior history). Used only to establish
    /// <c>AIAgent.CurrentRunContext.RequestMessages</c> for context providers — see
    /// <see cref="AgentRunContextScope"/> — never fed into the <c>AIContext</c> accumulator itself
    /// (that would duplicate the message once <see cref="CallLlmActivity"/> also sends it).
    /// </summary>
    public List<WorkflowChatMessage> RequestMessages { get; init; } = [];

    /// <summary>
    /// The <see cref="AgentRunOptions"/> supplied for this run (e.g. caller-supplied
    /// <see cref="AgentRunOptions.AdditionalProperties"/> such as a session identifier an external
    /// memory provider needs). Established as <c>AIAgent.CurrentRunContext.RunOptions</c> for the
    /// duration of the provider loop — see <see cref="AgentRunContextScope"/>.
    /// </summary>
    public AgentRunOptions? Options { get; init; }
}
