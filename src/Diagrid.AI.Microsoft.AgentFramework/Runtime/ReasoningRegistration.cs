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
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// A reasoning effort for the LLM calls <see cref="CallLlmActivity"/> makes, registered in DI by
/// <c>WithReasoning(...)</c>. A <see langword="null"/> <see cref="AgentName"/> sets the default for
/// every agent; a named registration overrides it for that agent.
/// </summary>
/// <param name="AgentName">The agent the effort applies to, or <see langword="null"/> for all agents.</param>
/// <param name="Effort">The reasoning effort to request.</param>
/// <param name="Output">The reasoning output to request, or <see langword="null"/> for the provider's default.</param>
internal sealed record ReasoningEffortRegistration(string? AgentName, ReasoningEffort Effort, ReasoningOutput? Output)
    : IAgentScopedRegistration
{
    /// <summary>
    /// Returns the reasoning options for <paramref name="agentName"/>. The effort and the output are
    /// each taken, in order of precedence, from the last registration naming the agent, then the
    /// agent's own <see cref="ChatOptions.Reasoning"/>, then the last default registration;
    /// <see langword="null"/> when none sets either (the provider's default).
    /// </summary>
    /// <param name="registrations">The registered efforts, in registration order.</param>
    /// <param name="agentName">The agent making the LLM call.</param>
    /// <param name="agentOptions">The reasoning set on the agent's own <see cref="ChatOptions"/>, if any.</param>
    internal static ReasoningOptions? Resolve(
        IEnumerable<ReasoningEffortRegistration> registrations,
        string agentName,
        ReasoningOptions? agentOptions)
    {
        var (named, fallback) = AgentScopedRegistration.Find(registrations, agentName);

        // A new instance, so the call never shares the agent's registered options.
        var effort = named?.Effort ?? agentOptions?.Effort ?? fallback?.Effort;
        var output = named?.Output ?? agentOptions?.Output ?? fallback?.Output;
        return effort is null && output is null ? null : new ReasoningOptions { Effort = effort, Output = output };
    }

    /// <summary>Throws unless <paramref name="effort"/> and <paramref name="output"/> are defined values.</summary>
    internal static void Validate(ReasoningEffort effort, ReasoningOutput? output)
    {
        if (!Enum.IsDefined(effort))
        {
            throw new ArgumentOutOfRangeException(nameof(effort), effort, "Unknown reasoning effort.");
        }

        if (output is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(output), output, "Unknown reasoning output.");
        }
    }
}

/// <summary>
/// How much earlier reasoning <see cref="CallLlmActivity"/> sends back, registered in DI by
/// <c>WithReasoningReplay(...)</c>. A <see langword="null"/> <see cref="AgentName"/> sets the default
/// for every agent; a named registration overrides it for that agent.
/// </summary>
/// <param name="AgentName">The agent the setting applies to, or <see langword="null"/> for all agents.</param>
/// <param name="Replay">How much reasoning to send back.</param>
internal sealed record ReasoningReplayRegistration(string? AgentName, ReasoningReplay Replay) : IAgentScopedRegistration
{
    /// <summary>
    /// Returns the setting for <paramref name="agentName"/>: the last registration naming it, else
    /// the last default registration, else <see cref="ReasoningReplay.AcrossTurns"/>.
    /// </summary>
    internal static ReasoningReplay Resolve(IEnumerable<ReasoningReplayRegistration> registrations, string agentName)
    {
        return AgentScopedRegistration.Resolve(registrations, agentName)?.Replay ?? ReasoningReplay.AcrossTurns;
    }

    /// <summary>Throws unless <paramref name="replay"/> is a defined value.</summary>
    internal static void Validate(ReasoningReplay replay)
    {
        if (!Enum.IsDefined(replay))
        {
            throw new ArgumentOutOfRangeException(nameof(replay), replay, "Unknown reasoning replay setting.");
        }
    }
}
