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

using System.Globalization;
using System.Text.Json;
using Dapr.StateManagement;
using Grpc.Core;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

/// <summary>
/// <see cref="IConversationStateBackend"/> over <see cref="DaprStateManagementClient"/>.
/// </summary>
/// <remarks>
/// The Dapr client returns a default value instead of throwing when its call is cancelled, which here would
/// read as "absent" or "ETag mismatch". Every call is therefore followed by a cancellation check.
/// </remarks>
internal sealed class DaprConversationStateBackend(
    DaprStateManagementClient client,
    DaprConversationStoreOptions options) : IConversationStateBackend
{
    private static readonly StateOptions StrongFirstWrite = new()
    {
        Consistency = ConsistencyMode.Strong,
        Concurrency = ConcurrencyMode.FirstWrite,
    };

    private readonly string _storeName = options.StateStoreName;

    public async Task<(JsonElement? Value, string? ETag)> GetAsync(string key, CancellationToken cancellationToken)
    {
        var (value, etag) = await client.GetStateAndETagAsync<JsonElement?>(
            _storeName, key, ConsistencyMode.Strong, metadata: null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return IsPresent(value) ? (value, etag) : (null, null);
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetBulkAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, JsonElement>(keys.Count, StringComparer.Ordinal);
        if (keys.Count == 0)
        {
            return found;
        }

        var items = await client.GetBulkStateAsync<JsonElement?>(
            _storeName, keys, parallelism: null, metadata: null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var item in items)
        {
            if (IsPresent(item.Value))
            {
                found[item.Key] = item.Value!.Value;
            }
        }

        // The client drops each item's error, so a key the store failed to read looks absent. Absent keys are
        // rare (a concurrent fold), so each is read again on its own, which throws if the store fails.
        foreach (var key in keys)
        {
            if (!found.ContainsKey(key))
            {
                var (value, _) = await GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (value is { } present)
                {
                    found[key] = present;
                }
            }
        }

        return found;
    }

    public async Task SaveAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken)
    {
        await client.SaveStateAsync<JsonElement?>(
            _storeName, key, value, stateOptions: null, Metadata(ttlSeconds), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> TrySaveAsync(
        string key, JsonElement value, string etag, int? ttlSeconds, CancellationToken cancellationToken)
    {
        var saved = await client.TrySaveStateAsync<JsonElement?>(
            _storeName, key, value, etag, StrongFirstWrite, Metadata(ttlSeconds), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return saved;
    }

    public async Task<bool> TryCreateAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken)
    {
        // First-write concurrency without an ETag writes only when the key does not exist yet. Stores report
        // the conflict differently: daprd maps an ETag error to Aborted, but Redis fails its create-only
        // script with a plain Internal error. So after any failure the key is read: if it exists, this create
        // can no longer land and the call reports a conflict. The store's next read of the manifest then
        // tells whether it is another writer's or this one's, with a lost reply.
        try
        {
            await client.SaveStateAsync<JsonElement?>(
                _storeName, key, value, StrongFirstWrite, Metadata(ttlSeconds), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsConflict(ex))
            {
                return false;
            }

            var (existing, _) = await GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return false;
            }

            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    public async Task DeleteBulkAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var items = keys.Select(k => new BulkDeleteStateItem(k)).ToList();
        await client.DeleteBulkStateAsync(_storeName, items, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsPresent(JsonElement? value) =>
        value is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };

    private static Dictionary<string, string>? Metadata(int? ttlSeconds) =>
        ttlSeconds is { } ttl
            ? new Dictionary<string, string> { ["ttlInSeconds"] = ttl.ToString(CultureInfo.InvariantCulture) }
            : null;

    /// <summary>
    /// Whether a failed write was rejected because of a concurrency conflict. The sidecar reports an ETag
    /// mismatch as <see cref="StatusCode.Aborted"/>; the Dapr client wraps it in a <c>DaprException</c>.
    /// </summary>
    private static bool IsConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is RpcException { StatusCode: StatusCode.Aborted })
            {
                return true;
            }
        }

        return false;
    }
}
