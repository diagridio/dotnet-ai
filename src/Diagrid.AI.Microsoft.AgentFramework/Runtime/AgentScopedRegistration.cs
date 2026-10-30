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
/// A setting registered either for every agent (a <see langword="null"/> <see cref="AgentName"/>) or for one agent,
/// such as <c>WithLlmTimeout</c>, <c>WithReasoning</c> or <c>WithContextWindow</c>.
/// </summary>
internal interface IAgentScopedRegistration
{
    /// <summary>The agent the setting applies to, or <see langword="null"/> for every agent.</summary>
    string? AgentName { get; }
}

/// <summary>
/// Picks the registration that applies to an agent: the last one naming it (case-insensitive), else the last one
/// for every agent. One rule for every per-agent setting.
/// </summary>
internal static class AgentScopedRegistration
{
    /// <summary>The last registration naming <paramref name="agentName"/>, and the last default one.</summary>
    internal static (T? Named, T? Default) Find<T>(IEnumerable<T>? registrations, string agentName)
        where T : class, IAgentScopedRegistration
    {
        T? named = null;
        T? @default = null;
        foreach (var registration in registrations ?? [])
        {
            if (registration.AgentName is null)
            {
                @default = registration;
            }
            else if (string.Equals(registration.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
            {
                named = registration;
            }
        }

        return (named, @default);
    }

    /// <summary>The registration that applies to <paramref name="agentName"/>, or <see langword="null"/>.</summary>
    internal static T? Resolve<T>(IEnumerable<T>? registrations, string agentName)
        where T : class, IAgentScopedRegistration
    {
        var (named, @default) = Find(registrations, agentName);
        return named ?? @default;
    }
}
