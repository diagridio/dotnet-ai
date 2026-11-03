// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations;

/// <summary>
/// What a conditional write does when a test intercepts it.
/// </summary>
internal enum CommitFault
{
    /// <summary>The write behaves normally.</summary>
    None,

    /// <summary>The write lands, then the call throws, as when the reply is lost.</summary>
    LandThenThrow,

    /// <summary>The call throws and the write is held, to be delivered later with <see cref="InMemoryConversationStateBackend.DeliverHeldWrites"/>.</summary>
    ThrowAndHold,

    /// <summary>The call throws and the write never lands.</summary>
    ThrowAndDrop,
}

/// <summary>
/// An in-memory state store with Redis's ETag semantics (a version per key that only increases, including
/// across deletes, and a conditioned save that also lands on a missing key), TTL metadata, and hooks to
/// inject faults and concurrent writers.
/// </summary>
internal sealed class InMemoryConversationStateBackend : IConversationStateBackend
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Stored> _items = new(StringComparer.Ordinal);
    private readonly List<Func<bool>> _heldWrites = [];
    private long _version;

    /// <summary>Every operation, in order, as "op:key".</summary>
    public List<string> Log { get; } = [];

    /// <summary>Called after a manifest-style read (<see cref="GetAsync"/>) returns, before the caller continues.</summary>
    public Func<string, Task>? AfterGet { get; set; }

    /// <summary>Called before a conditional write is applied, outside the lock.</summary>
    public Func<string, Task>? BeforeCommit { get; set; }

    /// <summary>Chooses a fault for each conditional write (<see cref="TrySaveAsync"/>, <see cref="TryCreateAsync"/>).</summary>
    public Func<string, CommitFault>? CommitFaults { get; set; }

    /// <summary>Called before each bulk read with its keys.</summary>
    public Action<IReadOnlyList<string>>? BeforeGetBulk { get; set; }

    /// <summary>Throws from <see cref="SaveAsync"/> for the keys it returns true for.</summary>
    public Func<string, bool>? FailSave { get; set; }

    /// <summary>When true, reads return no ETag, like a store without ETag support.</summary>
    public bool OmitETags { get; set; }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _items.Keys.ToList();
            }
        }
    }

    public int? TtlOf(string key)
    {
        lock (_gate)
        {
            return _items[key].TtlSeconds;
        }
    }

    public string? ETagOf(string key)
    {
        lock (_gate)
        {
            return _items.TryGetValue(key, out var item) ? item.Version.ToString() : null;
        }
    }

    public JsonElement Raw(string key)
    {
        lock (_gate)
        {
            return _items[key].Value;
        }
    }

    public void Put(string key, JsonElement value)
    {
        lock (_gate)
        {
            _items[key] = new Stored(value.Clone(), ++_version, null);
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _items.Remove(key);
        }
    }

    /// <summary>Delivers the held writes; returns how many of them landed.</summary>
    public int DeliverHeldWrites()
    {
        List<Func<bool>> held;
        lock (_gate)
        {
            held = [.. _heldWrites];
            _heldWrites.Clear();
        }

        return held.Count(write => write());
    }

    public async Task<(JsonElement? Value, string? ETag)> GetAsync(string key, CancellationToken cancellationToken)
    {
        (JsonElement? Value, string? ETag) result;
        lock (_gate)
        {
            Log.Add($"get:{key}");
            result = _items.TryGetValue(key, out var item)
                ? (item.Value.Clone(), OmitETags ? null : item.Version.ToString())
                : (null, null);
        }

        if (AfterGet is { } afterGet)
        {
            await afterGet(key);
        }

        return result;
    }

    public Task<IReadOnlyDictionary<string, JsonElement>> GetBulkAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        BeforeGetBulk?.Invoke(keys);
        lock (_gate)
        {
            Log.Add($"getbulk:{string.Join(",", keys)}");
            var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (_items.TryGetValue(key, out var item))
                {
                    found[key] = item.Value.Clone();
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(found);
        }
    }

    public Task SaveAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken)
    {
        if (FailSave?.Invoke(key) == true)
        {
            throw new InvalidOperationException($"Injected save failure for '{key}'.");
        }

        lock (_gate)
        {
            Log.Add($"save:{key}");
            _items[key] = new Stored(value.Clone(), ++_version, ttlSeconds);
        }

        return Task.CompletedTask;
    }

    // Like Redis: a save conditioned on an ETag also lands when the key does not exist.
    public Task<bool> TrySaveAsync(string key, JsonElement value, string etag, int? ttlSeconds, CancellationToken cancellationToken) =>
        Conditional(key, value, ttlSeconds, "trysave", existing => existing is null || existing.Version.ToString() == etag);

    public Task<bool> TryCreateAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken) =>
        Conditional(key, value, ttlSeconds, "trycreate", existing => existing is null);

    public Task DeleteBulkAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var key in keys)
            {
                Log.Add($"delete:{key}");
                _items.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private async Task<bool> Conditional(string key, JsonElement value, int? ttlSeconds, string op, Func<Stored?, bool> condition)
    {
        if (BeforeCommit is { } beforeCommit)
        {
            await beforeCommit(key);
        }

        var copy = value.Clone();
        bool Apply()
        {
            lock (_gate)
            {
                _items.TryGetValue(key, out var existing);
                if (!condition(existing))
                {
                    Log.Add($"{op}-rejected:{key}");
                    return false;
                }

                Log.Add($"{op}:{key}");
                _items[key] = new Stored(copy, ++_version, ttlSeconds);
                return true;
            }
        }

        switch (CommitFaults?.Invoke(key) ?? CommitFault.None)
        {
            case CommitFault.LandThenThrow:
                Apply();
                throw new TimeoutException("Injected: the write landed but the reply was lost.");
            case CommitFault.ThrowAndHold:
                lock (_gate)
                {
                    _heldWrites.Add(Apply);
                }

                throw new TimeoutException("Injected: the write is delayed.");
            case CommitFault.ThrowAndDrop:
                throw new TimeoutException("Injected: the write was lost.");
            default:
                return Apply();
        }
    }

    private sealed record Stored(JsonElement Value, long Version, int? TtlSeconds);
}

/// <summary>A clock the test sets.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
