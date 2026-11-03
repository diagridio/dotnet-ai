// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Dapr;
using Dapr.StateManagement;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;
using Grpc.Core;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations;

/// <summary>
/// Verifies <see cref="DaprConversationStateBackend"/> against a fake Dapr state client that behaves like the
/// real one where it matters: it returns defaults instead of throwing when cancelled, and wraps sidecar
/// errors in <see cref="DaprException"/>.
/// </summary>
public sealed class DaprConversationStateBackendTests
{
    private static readonly JsonElement Value = JsonSerializer.SerializeToElement(new { n = 1 });

    private readonly FakeStateClient _client = new();

    private DaprConversationStateBackend CreateBackend() =>
        new(_client, new DaprConversationStoreOptions { StateStoreName = "statestore" });

    [Fact]
    public async Task Get_MissingKey_ReturnsNoValueAndNoETag()
    {
        var (value, etag) = await CreateBackend().GetAsync("missing", TestContext.Current.CancellationToken);

        Assert.Null(value);
        Assert.Null(etag);
    }

    [Fact]
    public async Task Get_ReadsWithStrongConsistency_AndReturnsTheETag()
    {
        _client.Items["k"] = (Value, "3");

        var (value, etag) = await CreateBackend().GetAsync("k", TestContext.Current.CancellationToken);

        Assert.Equal(1, value!.Value.GetProperty("n").GetInt32());
        Assert.Equal("3", etag);
        Assert.Equal(ConsistencyMode.Strong, _client.LastConsistency);
    }

    [Fact]
    public async Task GetBulk_LeavesOutKeysReturnedWithoutAValue()
    {
        _client.Items["a"] = (Value, "1");

        var found = await CreateBackend().GetBulkAsync(["a", "b"], TestContext.Current.CancellationToken);

        Assert.Equal(["a"], found.Keys);
    }

    [Fact]
    public async Task GetBulk_AKeyTheBulkReadFailedOn_IsReadAgainOnItsOwn()
    {
        // The client drops per-item errors: the bulk read returns the key without a value.
        _client.Items["a"] = (Value, "1");
        _client.BulkDrops.Add("a");

        var found = await CreateBackend().GetBulkAsync(["a"], TestContext.Current.CancellationToken);

        Assert.Equal(["a"], found.Keys);
    }

    [Fact]
    public async Task GetBulk_AKeyThatFailsOnItsOwnToo_Throws()
    {
        _client.BulkDrops.Add("a");
        _client.GetError = new DaprException("State operation failed.");

        await Assert.ThrowsAsync<DaprException>(
            () => CreateBackend().GetBulkAsync(["a"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_SendsTtlMetadataOnlyWhenThereIsATtl()
    {
        var backend = CreateBackend();

        await backend.SaveAsync("a", Value, ttlSeconds: 30, TestContext.Current.CancellationToken);
        Assert.Equal("30", _client.LastMetadata!["ttlInSeconds"]);

        await backend.SaveAsync("b", Value, ttlSeconds: null, TestContext.Current.CancellationToken);
        Assert.Null(_client.LastMetadata);
    }

    [Fact]
    public async Task TrySave_IsStrongAndFirstWrite_AndReturnsTheClientsAnswer()
    {
        _client.TrySaveResult = false;

        var saved = await CreateBackend().TrySaveAsync("k", Value, "7", ttlSeconds: 5, TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.Equal("7", _client.LastETag);
        Assert.Equal(ConsistencyMode.Strong, _client.LastOptions!.Consistency);
        Assert.Equal(ConcurrencyMode.FirstWrite, _client.LastOptions.Concurrency);
        Assert.Equal("5", _client.LastMetadata!["ttlInSeconds"]);
    }

    [Fact]
    public async Task TryCreate_Succeeds_WithFirstWriteAndNoETag()
    {
        Assert.True(await CreateBackend().TryCreateAsync("k", Value, null, TestContext.Current.CancellationToken));

        Assert.Equal(ConcurrencyMode.FirstWrite, _client.LastOptions!.Concurrency);
        Assert.True(_client.Items.ContainsKey("k"));
    }

    [Fact]
    public async Task TryCreate_AbortedConflict_IsFalse()
    {
        _client.SaveError = Wrap(StatusCode.Aborted);

        Assert.False(await CreateBackend().TryCreateAsync("k", Value, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryCreate_OtherError_WithTheKeyPresent_IsFalse()
    {
        // Redis reports a create-only conflict as Internal, from its Lua script.
        _client.Items["k"] = (Value, "1");
        _client.SaveError = Wrap(StatusCode.Internal);

        Assert.False(await CreateBackend().TryCreateAsync("k", Value, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryCreate_OtherError_WithTheKeyAbsent_IsRethrown()
    {
        _client.SaveError = Wrap(StatusCode.Unavailable);

        await Assert.ThrowsAsync<DaprException>(
            () => CreateBackend().TryCreateAsync("k", Value, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EveryCall_ThrowsWhenCancelled_ThoughTheClientReturnsDefaults()
    {
        _client.Items["k"] = (Value, "1");
        var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.GetAsync("k", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.GetBulkAsync(["k"], cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.SaveAsync("k", Value, null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.TrySaveAsync("k", Value, "1", null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.TryCreateAsync("n", Value, null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.DeleteBulkAsync(["k"], cts.Token));
        Assert.True(_client.Items.ContainsKey("k"));
    }

    private static DaprException Wrap(StatusCode code) =>
        new("State operation failed.", new RpcException(new Status(code, "injected")));

    /// <summary>
    /// A state client that, like the real one, returns defaults instead of throwing when its token is cancelled.
    /// </summary>
    private sealed class FakeStateClient : DaprStateManagementClient
    {
        public Dictionary<string, (JsonElement Value, string ETag)> Items { get; } = new(StringComparer.Ordinal);

        public Exception? SaveError { get; set; }

        public Exception? GetError { get; set; }

        /// <summary>Keys the bulk read returns without a value, as when the store failed on them.</summary>
        public HashSet<string> BulkDrops { get; } = new(StringComparer.Ordinal);

        public bool TrySaveResult { get; set; } = true;

        public ConsistencyMode? LastConsistency { get; private set; }

        public StateOptions? LastOptions { get; private set; }

        public IReadOnlyDictionary<string, string>? LastMetadata { get; private set; }

        public string? LastETag { get; private set; }

        public override Task<TValue?> GetStateAsync<TValue>(string storeName, string key, ConsistencyMode? consistencyMode = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
            where TValue : default =>
            throw new NotSupportedException();

        public override Task<(TValue? Value, string? ETag)> GetStateAndETagAsync<TValue>(string storeName, string key,
            ConsistencyMode? consistencyMode = null, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default) where TValue : default
        {
            LastConsistency = consistencyMode;
            if (GetError is not null && !cancellationToken.IsCancellationRequested)
            {
                throw GetError;
            }

            if (cancellationToken.IsCancellationRequested || !Items.TryGetValue(key, out var item))
            {
                return Task.FromResult<(TValue?, string?)>((default, null));
            }

            return Task.FromResult<(TValue?, string?)>(((TValue?)(object?)(JsonElement?)item.Value, item.ETag));
        }

        public override Task<IReadOnlyList<BulkStateItem<TValue>>> GetBulkStateAsync<TValue>(string storeName,
            IReadOnlyList<string> keys, int? parallelism = null, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult<IReadOnlyList<BulkStateItem<TValue>>>([]);
            }

            IReadOnlyList<BulkStateItem<TValue>> items = keys.Select(k => !BulkDrops.Contains(k) && Items.TryGetValue(k, out var item)
                ? new BulkStateItem<TValue>(k, (TValue)(object)(JsonElement?)item.Value, item.ETag)
                : new BulkStateItem<TValue>(k, default!, string.Empty)).ToList();
            return Task.FromResult(items);
        }

        public override Task SaveStateAsync<TValue>(string storeName, string key, TValue value, StateOptions? stateOptions = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            LastOptions = stateOptions;
            LastMetadata = metadata;
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            if (SaveError is not null)
            {
                throw SaveError;
            }

            Items[key] = (((JsonElement?)(object?)value)!.Value, "1");
            return Task.CompletedTask;
        }

        public override Task SaveBulkStateAsync<TValue>(string storeName, IReadOnlyList<SaveStateItem<TValue>> items,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public override Task<bool> TrySaveStateAsync<TValue>(string storeName, string key, TValue value, string etag,
            StateOptions? stateOptions = null, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = stateOptions;
            LastMetadata = metadata;
            LastETag = etag;
            return Task.FromResult(!cancellationToken.IsCancellationRequested && TrySaveResult);
        }

        public override Task DeleteStateAsync(string storeName, string key, StateOptions? stateOptions = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Items.Remove(key);
            }

            return Task.CompletedTask;
        }

        public override Task<bool> TryDeleteStateAsync(string storeName, string key, string etag, StateOptions? stateOptions = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task DeleteBulkStateAsync(string storeName, IReadOnlyList<BulkDeleteStateItem> items,
            CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var item in items)
                {
                    Items.Remove(item.Key);
                }
            }

            return Task.CompletedTask;
        }

        public override Task ExecuteStateTransactionAsync(string storeName, IReadOnlyList<StateTransactionRequest> operations,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<StateQueryResponse<TValue>> QueryStateAsync<TValue>(string storeName, string jsonQuery,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
