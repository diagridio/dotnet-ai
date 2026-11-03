// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Dapr.StateManagement;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Runs the conversation store (<c>WithConversationStore</c>) against a real daprd and Redis, and pins
/// the state store behavior it depends on: first-write without an ETag only creates, an ETag loser is
/// rejected, a missing key in a bulk read comes back without a value, and TTL metadata is honored.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class ConversationStoreRedisTests(DaprFixture fixture)
{
    private static readonly StateOptions FirstWrite = new()
    {
        Consistency = ConsistencyMode.Strong,
        Concurrency = ConcurrencyMode.FirstWrite,
    };

    private static string NewId(string name) => $"{name}-{Guid.NewGuid():N}";

    private static WorkflowChatMessage User(string text) => new() { Role = "user", Content = text };

    private static ConversationTurn Turn(string conversationId, string turnId, long baseSequence, params WorkflowChatMessage[] messages) =>
        new(conversationId, turnId, baseSequence, "planner", null, messages);

    // ── The state store facts the design depends on ────────────────────────

    [Fact]
    public async Task Redis_FirstWriteWithoutETag_OnlyCreates()
    {
        // Redis rejects the second write with a plain Internal error from its Lua script, not Aborted,
        // which is why the backend reads the key after a failed create instead of trusting the status.
        var key = NewId("first-write");
        var first = JsonSerializer.SerializeToElement(new { n = 1 });
        var second = JsonSerializer.SerializeToElement(new { n = 2 });

        await fixture.StateClient.SaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, first, FirstWrite, cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.StateClient.SaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, second, FirstWrite, cancellationToken: TestContext.Current.CancellationToken));

        var stored = await fixture.StateClient.GetStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, ConsistencyMode.Strong, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, stored!.Value.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Redis_StaleETag_IsRejected()
    {
        var key = NewId("etag");
        var value = JsonSerializer.SerializeToElement(new { n = 1 });
        await fixture.StateClient.SaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, value, cancellationToken: TestContext.Current.CancellationToken);
        var (_, etag) = await fixture.StateClient.GetStateAndETagAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, ConsistencyMode.Strong, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await fixture.StateClient.TrySaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, value, etag!, FirstWrite, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await fixture.StateClient.TrySaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, value, etag!, FirstWrite, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Redis_ETagConditionedSave_LandsOnAMissingKey()
    {
        // The reason the store never removes a manifest key and keeps an unknown commit's chunks while the
        // key is absent: a save conditioned on any ETag creates a missing key.
        var key = NewId("etag-missing");

        Assert.True(await fixture.StateClient.TrySaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, JsonSerializer.SerializeToElement(new { n = 1 }), "12345", FirstWrite,
            cancellationToken: TestContext.Current.CancellationToken));

        var (value, _) = await fixture.StateClient.GetStateAndETagAsync<JsonElement?>(
            DaprFixture.StateStoreName, key, ConsistencyMode.Strong, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(value);
    }

    [Fact]
    public async Task Redis_BulkReadOfAMissingKey_ReturnsTheKeyWithoutAValue()
    {
        var present = NewId("bulk-present");
        var missing = NewId("bulk-missing");
        await fixture.StateClient.SaveStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, present, JsonSerializer.SerializeToElement(new { n = 1 }),
            cancellationToken: TestContext.Current.CancellationToken);

        var items = await fixture.StateClient.GetBulkStateAsync<JsonElement?>(
            DaprFixture.StateStoreName, [present, missing], cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(Assert.Single(items, i => i.Key == present).Value);
        var absent = Assert.Single(items, i => i.Key == missing);
        Assert.True(absent.Value is null or { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null });
    }

    // ── The store against Redis ────────────────────────────────────────────

    [Fact]
    public async Task AppendLoadDelete_RoundTripsThroughRedis()
    {
        var store = fixture.ConversationStore;
        var id = NewId("roundtrip");
        var reply = new WorkflowChatMessage
        {
            Role = "assistant",
            Content = "hello",
            Reasoning = [new WorkflowReasoningContent { Text = "think", ProtectedData = "sig", Source = "planner" }],
        };

        await store.AppendTurnAsync(Turn(id, "t1", 0, User("hi"), reply), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn(id, "t2", 2, User("again")), TestContext.Current.CancellationToken);
        var again = await store.AppendTurnAsync(Turn(id, "t1", 0, User("hi"), reply), TestContext.Current.CancellationToken);

        Assert.Equal(ConversationAppendStatus.AlreadyAppended, again.Status);
        var snapshot = await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["hi", "hello", "again"], snapshot.Entries.Select(e => e.Message.Content));
        var reasoning = Assert.Single(snapshot.Entries[1].Message.Reasoning!);
        Assert.Equal(("think", "sig", "planner"), (reasoning.Text, reasoning.ProtectedData, reasoning.Source));
        Assert.Equal(2, (await store.LoadAsync(id, 2, cancellationToken: TestContext.Current.CancellationToken)).Entries.Count);

        Assert.True(await store.DeleteAsync(id, TestContext.Current.CancellationToken));
        Assert.Empty((await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken)).Entries);
        Assert.False(await store.DeleteAsync(id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadAsync(id, 1, cancellationToken: TestContext.Current.CancellationToken));

        // After a delete the same id starts a new conversation, under a new incarnation.
        var before = snapshot.Incarnation;
        await store.AppendTurnAsync(Turn(id, "n1", 0, User("new")), TestContext.Current.CancellationToken);
        var recreated = await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["new"], recreated.Entries.Select(e => e.Message.Content));
        Assert.NotEqual(before, recreated.Incarnation);
        await Assert.ThrowsAsync<ConversationNotFoundException>(
            () => store.LoadAsync(id, 1, before, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentFirstAppends_ExactlyOneCreatesTheManifest_AndBothTurnsAreStored()
    {
        var store = fixture.ConversationStore;
        for (var round = 0; round < 10; round++)
        {
            var id = NewId("first-race");
            using var barrier = new Barrier(2);
            Task<ConversationAppendResult> Append(string turnId) => Task.Run(() =>
            {
                barrier.SignalAndWait(TestContext.Current.CancellationToken);
                return store.AppendTurnAsync(Turn(id, turnId, 0, User(turnId)), TestContext.Current.CancellationToken);
            });
            var results = await Task.WhenAll(Append("a"), Append("b"));

            Assert.All(results, r => Assert.Equal(ConversationAppendStatus.Appended, r.Status));
            Assert.Single(results, r => r.BaseMoved);
            var snapshot = await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(["a", "b"], snapshot.Entries.Select(e => e.TurnId).Order());
        }
    }

    [Fact]
    public async Task ConcurrentAppends_AreAllStoredWithoutGaps()
    {
        var store = fixture.ConversationStore;
        var id = NewId("race");
        await store.AppendTurnAsync(Turn(id, "t0", 0, User("start")), TestContext.Current.CancellationToken);

        var turns = Enumerable.Range(1, 6).Select(i => Turn(id, $"t{i}", 1, User($"m{i}"), User($"n{i}"))).ToList();
        await Task.WhenAll(turns.Select(t => Task.Run(() => store.AppendTurnAsync(t, TestContext.Current.CancellationToken))));

        var snapshot = await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(13, snapshot.NextSequence);
        Assert.Equal(Enumerable.Range(0, 13).Select(i => (long)i), snapshot.Entries.Select(e => e.Sequence));

        // Each turn's two messages are adjacent: turns never interleave.
        foreach (var group in snapshot.Entries.Skip(1).Chunk(2))
        {
            Assert.Equal(group[0].TurnId, group[1].TurnId);
        }
    }

    [Fact]
    public async Task Ttl_TheConversationExpires()
    {
        await using var provider = CreateStoreProvider(o => o.TimeToLive = TimeSpan.FromSeconds(2));
        var store = provider.GetRequiredService<IConversationStore>();
        var id = NewId("ttl");

        await store.AppendTurnAsync(Turn(id, "t1", 0, User("short-lived")), TestContext.Current.CancellationToken);
        Assert.Single((await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken)).Entries);

        // Nothing reads it in between: a load after half the TTL would renew it.
        await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        var snapshot = await store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(snapshot.Entries);
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadAsync(id, 1, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ttl_AConversationThatKeepsBeingRead_DoesNotExpire()
    {
        await using var provider = CreateStoreProvider(o => o.TimeToLive = TimeSpan.FromSeconds(2));
        var store = provider.GetRequiredService<IConversationStore>();
        var id = NewId("ttl-read");

        await store.AppendTurnAsync(Turn(id, "t1", 0, User("kept alive")), TestContext.Current.CancellationToken);

        // Three times the TTL, reading it as a paused run's tool loop would.
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Single((await store.LoadAsync(id, 1, cancellationToken: TestContext.Current.CancellationToken)).Entries);
        }

        var appended = await store.AppendTurnAsync(Turn(id, "t2", 1, User("still here")), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.Appended, appended.Status);
    }

    /// <summary>
    /// A second store over the same sidecar, with its own settings.
    /// </summary>
    private static ServiceProvider CreateStoreProvider(Action<DaprConversationStoreOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDaprAgents().WithConversationStore(DaprFixture.StateStoreName, options =>
        {
            options.KeyPrefix = DaprFixture.ConversationKeyPrefix;
            configure(options);
        });
        return services.BuildServiceProvider();
    }
}
