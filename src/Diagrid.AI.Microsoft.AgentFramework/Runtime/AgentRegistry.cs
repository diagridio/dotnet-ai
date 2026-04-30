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

    // Tracks Lazy<AIAgent> instances that have been removed from _pendingRegistrations
    // but whose factory has not yet finished and been added to _mapByKey.
    // Concurrent threads use this to wait for an in-progress materialization instead of
    // incorrectly concluding that the agent is unregistered.
    private readonly ConcurrentDictionary<Lazy<AIAgent>, byte> _inProgressMaterializations = new();

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

            var lazyToMaterialize = pending.Value.Lazy;
            _inProgressMaterializations.TryAdd(lazyToMaterialize, 0);
            try
            {
                var agent = lazyToMaterialize.Value;
                var discoveredKey = new AgentKey(agent.Name!, pending.Value.ChatClientKey);
                _mapByKey.GetOrAdd(discoveredKey, _ => lazyToMaterialize);

                if (AgentKeyComparer.Comparer.Equals(agent.Name, name))
                {
                    return agent;
                }
            }
            finally
            {
                _inProgressMaterializations.TryRemove(lazyToMaterialize, out _);
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
        Lazy<AIAgent>? match = null;

        // Process pending registrations one at a time so that each agent is added to
        // _mapByKey immediately after materialization. This prevents a race where another
        // thread clears the entire pending list, leaving concurrent callers unable to find
        // agents that are still being materialized outside the lock.
        while (true)
        {
            PendingRegistration pending;
            lock (_gate)
            {
                if (_pendingRegistrations.Count == 0)
                    break;

                pending = _pendingRegistrations[0];
                _pendingRegistrations.RemoveAt(0);
            }

            var lazyToMaterialize = pending.Lazy;
            _inProgressMaterializations.TryAdd(lazyToMaterialize, 0);
            try
            {
                var agent = lazyToMaterialize.Value;
                var discoveredKey = new AgentKey(agent.Name!, pending.ChatClientKey);
                _mapByKey.GetOrAdd(discoveredKey, _ => lazyToMaterialize);

                if (!AgentKeyComparer.Comparer.Equals(agent.Name, name))
                {
                    continue;
                }

                if (match is null)
                {
                    match = lazyToMaterialize;
                }
                else
                {
                    ambiguous = true;
                }
            }
            finally
            {
                _inProgressMaterializations.TryRemove(lazyToMaterialize, out _);
            }
        }

        // Also wait on any Lazy instances currently being materialized by concurrent threads.
        // These were removed from _pendingRegistrations but not yet promoted to _mapByKey,
        // so they would be invisible to the caller's subsequent TryGetSingleMatchByName check.
        foreach (var (inProgressLazy, _) in _inProgressMaterializations)
        {
            // Lazy.Value blocks until the materializing thread's factory completes.
            var agent = inProgressLazy.Value;
            if (!AgentKeyComparer.Comparer.Equals(agent.Name, name))
                continue;

            if (match is null)
                match = inProgressLazy;
            else
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
