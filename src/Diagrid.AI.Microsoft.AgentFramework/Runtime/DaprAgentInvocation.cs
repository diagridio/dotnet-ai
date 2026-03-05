// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Payload used to pass agent invocation data to the activity (non-deterministic execution).
/// </summary>
/// <param name="AgentName">The name of the invoked agent.</param>
/// <param name="Message">The message being sent to the activity.</param>
/// <param name="Thread">The current agent thread.</param>
/// <param name="Options">Options relevant to performing the agent run operation.</param>
public sealed record DaprAgentInvocation(
    string AgentName,
    string? Message,
    AgentThread? Thread,
    AgentRunOptions? Options)
{
    /// <summary>
    /// Optional chat client key used to select a keyed agent registration.
    /// </summary>
    public string? ChatClientKey { get; init; }
}
