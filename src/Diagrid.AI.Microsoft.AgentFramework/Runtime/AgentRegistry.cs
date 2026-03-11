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

using System.Collections.Concurrent;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Internal registry that holds lazy agent factories by name.
/// </summary>
public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<AgentKey, Lazy<AIAgent>> _mapByKey = new(new AgentKeyComparer());
    private readonly List<PendingRegistration> _pendingRegistrations = [];
    private readonly object _gate = new();
    private IServiceProvider? _provider;

    /// <summary>
    /// Used to initialize the registry with pre-registered agent factories.
    /// </summary>
    public AgentRegistry(IServiceProvider provider, IEnumerable<AgentFactoryRegistration> registrations)
    {
        _provider = provider;
        foreach (var reg in registrations)
        {
            AddFactory(reg.Factory, reg.ChatClientKey, reg.Name, provider);
        }
    }

    /// <summary>
    /// Registers an agent factory, optionally providing an explicit agent name.
    /// </summary>
    /// <param name="factory">The factory to register</param>
    /// <param name="chatClientKey">An optional key used to identify the chat client.</param>
    /// <param name="agentName">The explicit agent name to use, if provided</param>
    /// <param name="provider">The service provider to use for agent creation</param>
    public void AddFactory(
        Func<IServiceProvider, AIAgent> factory,
        string? chatClientKey,
        string? agentName,
        IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureInitialized(provider);

        var lazy = new Lazy<AIAgent>(() => CreateAndValidateAgent(factory, provider), isThreadSafe: true);

        if (!string.IsNullOrWhiteSpace(agentName))
        {
            var key = new AgentKey(agentName, chatClientKey);
            _mapByKey.GetOrAdd(key, _ => lazy);
            return;
        }

        lock (_gate)
        {
            _pendingRegistrations.Add(new PendingRegistration(lazy, chatClientKey));
        }
    }
    
    /// <summary>
    /// Ensures the registry has a reference to the root <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    public void EnsureInitialized(IServiceProvider provider) => _provider ??= provider;

    /// <summary>
    /// Returns the set of registered agent names.
    /// </summary>
    public IEnumerable<string> RegisteredNames =>
        _mapByKey.Keys.Select(key => key.Name).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or creates the <see cref="AIAgent"/> registered under the specified agent name.
    /// </summary>
    /// <param name="name">The name of the agent.</param>
    /// <param name="provider">The service provider.</param>
    /// <returns>The <see cref="AIAgent"/> instance.</returns>
    public AIAgent Get(string name, IServiceProvider provider) => Get(name, chatClientKey: null, provider);

    /// <summary>
    /// Gets or creates the <see cref="AIAgent"/> registered under the specified agent name and chat client key.
    /// </summary>
    /// <param name="name">The name of the agent.</param>
    /// <param name="chatClientKey">Optional chat client key.</param>
    /// <param name="provider">The service provider.</param>
    /// <returns>The <see cref="AIAgent"/> instance.</returns>
    public AIAgent Get(string name, string? chatClientKey, IServiceProvider provider)
    {
        EnsureInitialized(provider);
        var key = new AgentKey(name, chatClientKey);
        if (_mapByKey.TryGetValue(key, out var lazy))
        {
            return lazy.Value;
        }

        var resolved = TryResolvePending(name, chatClientKey);
        if (resolved is not null)
        {
            return resolved;
        }

        if (string.IsNullOrWhiteSpace(chatClientKey))
        {
            var resolvedByName = TryResolveByNameOnly(name);
            if (resolvedByName is not null)
            {
                return resolvedByName;
            }
        }

        {
            var suffix = string.IsNullOrWhiteSpace(chatClientKey)
                ? string.Empty
                : $" with chat client key '{chatClientKey}'";
            throw new InvalidOperationException($"Agent '{name}'{suffix} is not registered. " +
                                                "Ensure you called builder.Services.AddDaprAgents(...).");
        }
    }

    private AIAgent? TryResolvePending(string name, string? chatClientKey)
    {
        while (true)
        {
            PendingRegistration? pending = null;
            lock (_gate)
            {
                if (_mapByKey.TryGetValue(new AgentKey(name, chatClientKey), out var lazy))
                {
                    return lazy.Value;
                }

                for (var i = 0; i < _pendingRegistrations.Count; i++)
                {
                    var candidate = _pendingRegistrations[i];
                    if (!ChatClientKeyEquals(candidate.ChatClientKey, chatClientKey))
                    {
                        continue;
                    }

                    pending = candidate;
                    _pendingRegistrations.RemoveAt(i);
                    break;
                }
            }

            if (pending is null)
            {
                return null;
            }

            var agent = pending.Value.Lazy.Value;
            var discoveredKey = new AgentKey(agent.Name!, pending.Value.ChatClientKey);
            _mapByKey.GetOrAdd(discoveredKey, _ => pending.Value.Lazy);

            if (AgentKeyComparer.Comparer.Equals(agent.Name, name))
            {
                return agent;
            }
        }
    }

    private AIAgent? TryResolveByNameOnly(string name)
    {
        if (TryGetSingleMatchByName(name, out var lazy, out var ambiguous))
        {
            return lazy.Value;
        }

        if (ambiguous)
        {
            ThrowAmbiguousName(name);
        }

        var pendingResolved = TryResolvePendingByName(name, out ambiguous);
        if (pendingResolved is not null)
        {
            return pendingResolved;
        }

        if (ambiguous)
        {
            ThrowAmbiguousName(name);
        }

        if (TryGetSingleMatchByName(name, out lazy, out ambiguous))
        {
            return lazy.Value;
        }

        if (ambiguous)
        {
            ThrowAmbiguousName(name);
        }

        return null;
    }

    private bool TryGetSingleMatchByName(string name, out Lazy<AIAgent> lazy, out bool ambiguous)
    {
        lazy = null!;
        ambiguous = false;
        var found = false;

        foreach (var entry in _mapByKey)
        {
            if (!AgentKeyComparer.Comparer.Equals(entry.Key.Name, name))
            {
                continue;
            }

            if (!found)
            {
                lazy = entry.Value;
                found = true;
                continue;
            }

            ambiguous = true;
            return false;
        }

        return found;
    }

    private AIAgent? TryResolvePendingByName(string name, out bool ambiguous)
    {
        ambiguous = false;
        List<PendingRegistration> pendingSnapshot;

        lock (_gate)
        {
            if (_pendingRegistrations.Count == 0)
            {
                return null;
            }

            pendingSnapshot = new List<PendingRegistration>(_pendingRegistrations);
            _pendingRegistrations.Clear();
        }

        Lazy<AIAgent>? match = null;
        foreach (var pending in pendingSnapshot)
        {
            var agent = pending.Lazy.Value;
            var discoveredKey = new AgentKey(agent.Name!, pending.ChatClientKey);
            _mapByKey.GetOrAdd(discoveredKey, _ => pending.Lazy);

            if (!AgentKeyComparer.Comparer.Equals(agent.Name, name))
            {
                continue;
            }

            if (match is null)
            {
                match = pending.Lazy;
                continue;
            }

            ambiguous = true;
        }

        return match?.Value;
    }

    private static void ThrowAmbiguousName(string name)
    {
        throw new InvalidOperationException(
            $"Agent '{name}' is registered with multiple chat client keys. " +
            "Provide a chat client key to disambiguate.");
    }

    private static AIAgent CreateAndValidateAgent(Func<IServiceProvider, AIAgent> factory, IServiceProvider provider)
    {
        var agent = factory(provider);
        if (agent is null)
        {
            throw new InvalidOperationException("Agent factory returned null or an agent without a valid Name.");
        }

        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            throw new InvalidOperationException(
                "AIAgent.Name is null or empty. Provide a non-empty name when creating the agent");
        }

        return agent;
    }

    private readonly struct AgentKey(string name, string? chatClientKey)
    {
        public string Name { get; } = name;
        public string? ChatClientKey { get; } = chatClientKey;
    }

    private sealed class AgentKeyComparer : IEqualityComparer<AgentKey>
    {
        internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        public bool Equals(AgentKey x, AgentKey y) =>
            Comparer.Equals(x.Name, y.Name) &&
            Comparer.Equals(x.ChatClientKey ?? string.Empty, y.ChatClientKey ?? string.Empty);

        public int GetHashCode(AgentKey obj)
        {
            var nameHash = Comparer.GetHashCode(obj.Name);
            var keyHash = Comparer.GetHashCode(obj.ChatClientKey ?? string.Empty);
            return HashCode.Combine(nameHash, keyHash);
        }
    }

    private static bool ChatClientKeyEquals(string? left, string? right) =>
        AgentKeyComparer.Comparer.Equals(left ?? string.Empty, right ?? string.Empty);

    private readonly record struct PendingRegistration(Lazy<AIAgent> Lazy, string? ChatClientKey);
}
