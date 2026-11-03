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
/// A timeout for the LLM calls <see cref="CallLlmActivity"/> makes, registered in DI by
/// <c>WithLlmTimeout(...)</c>. A <see langword="null"/> <see cref="AgentName"/> sets the default
/// for every agent; a named registration overrides it for that agent.
/// </summary>
/// <param name="AgentName">The agent the timeout applies to, or <see langword="null"/> for all agents.</param>
/// <param name="Timeout">The timeout, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for none.</param>
internal sealed record LlmTimeoutRegistration(string? AgentName, TimeSpan Timeout)
{
    /// <summary>
    /// The timeout used when nothing is registered. It matches the <see cref="HttpClient.Timeout"/>
    /// default that used to bound every Dapr Conversation call, so agents that never call
    /// <c>WithLlmTimeout(...)</c> keep the behaviour they had.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    /// <summary>
    /// The largest finite timeout accepted, bounded by <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>
    /// and <see cref="HttpClient.Timeout"/>, which both reject anything above <see cref="int.MaxValue"/> milliseconds.
    /// </summary>
    internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    /// Returns the timeout for <paramref name="agentName"/>: the last registration naming it, else the
    /// last default registration, else <see cref="DefaultTimeout"/>.
    /// </summary>
    /// <param name="registrations">The registered timeouts, in registration order.</param>
    /// <param name="agentName">The agent making the LLM call.</param>
    /// <returns>The timeout to apply, possibly <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.</returns>
    internal static TimeSpan Resolve(IEnumerable<LlmTimeoutRegistration> registrations, string agentName)
    {
        TimeSpan? fallback = null;
        TimeSpan? named = null;

        foreach (var registration in registrations)
        {
            if (registration.AgentName is null)
            {
                fallback = registration.Timeout;
            }
            else if (string.Equals(registration.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
            {
                named = registration.Timeout;
            }
        }

        return named ?? fallback ?? DefaultTimeout;
    }

    /// <summary>
    /// Throws unless <paramref name="timeout"/> is positive and at most <see cref="MaxTimeout"/>, or is
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    /// <param name="timeout">The timeout to validate.</param>
    /// <param name="paramName">The name of the parameter that carried it.</param>
    internal static void Validate(TimeSpan timeout, string paramName)
    {
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return;
        }

        if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
        {
            throw new ArgumentOutOfRangeException(paramName, timeout,
                $"The LLM timeout must be positive and at most {MaxTimeout}, or Timeout.InfiniteTimeSpan.");
        }
    }
}
