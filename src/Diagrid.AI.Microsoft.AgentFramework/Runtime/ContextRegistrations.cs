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

using Diagrid.AI.Microsoft.AgentFramework.Conversations;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// The context window of an agent's model, registered by <c>WithContextWindow(...)</c>. A <see langword="null"/>
/// <see cref="AgentName"/> sets the default for every agent; a named registration overrides it for that agent.
/// </summary>
/// <param name="AgentName">The agent it applies to, or <see langword="null"/> for all agents.</param>
/// <param name="MaxContextTokens">The model's context window, in tokens.</param>
/// <param name="MaxOutputTokens">The tokens kept free for the model's reply.</param>
internal sealed record ContextWindowRegistration(string? AgentName, int MaxContextTokens, int MaxOutputTokens)
    : IAgentScopedRegistration
{
    /// <summary>
    /// The registration for <paramref name="agentName"/>: the last one naming it, else the last default, else none.
    /// </summary>
    internal static ContextWindowRegistration? Resolve(IEnumerable<ContextWindowRegistration> registrations, string agentName) =>
        AgentScopedRegistration.Resolve(registrations, agentName);

    /// <summary>
    /// Throws unless the window is positive and leaves room for input after the reserved output.
    /// </summary>
    internal static void Validate(int maxContextTokens, int maxOutputTokens)
    {
        if (maxContextTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxContextTokens), maxContextTokens, "The context window must be positive.");
        }

        if (maxOutputTokens < 0 || maxOutputTokens >= maxContextTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens), maxOutputTokens,
                "The reserved output must be zero or more, and smaller than the context window.");
        }
    }
}

/// <summary>
/// Conversation compaction for an agent, registered by <c>WithConversationCompaction(...)</c>. A
/// <see langword="null"/> <see cref="AgentName"/> sets the default for every agent.
/// </summary>
/// <param name="AgentName">The agent it applies to, or <see langword="null"/> for all agents.</param>
/// <param name="Options">The compaction settings.</param>
internal sealed record ConversationCompactionRegistration(string? AgentName, ConversationCompactionOptions Options)
    : IAgentScopedRegistration
{
    /// <summary>
    /// The registration for <paramref name="agentName"/>: the last one naming it, else the last default, else none.
    /// </summary>
    internal static ConversationCompactionRegistration? Resolve(
        IEnumerable<ConversationCompactionRegistration> registrations, string agentName) =>
        AgentScopedRegistration.Resolve(registrations, agentName);
}
