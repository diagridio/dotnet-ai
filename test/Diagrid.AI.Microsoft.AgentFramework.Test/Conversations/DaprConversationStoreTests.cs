// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations;

/// <summary>
/// Verifies <see cref="DaprConversationStore"/> over an in-memory backend with real ETag semantics:
/// the Python <c>DaprSessionStore</c> suite ported, plus the deletion rule, idempotent turns and pinned loads.
/// </summary>
public sealed class DaprConversationStoreTests
{
    private const string ConversationId = "conv-1";
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryConversationStateBackend _backend = new();
    private readonly ManualTimeProvider _clock = new(Start);

    private DaprConversationStore CreateStore(Action<DaprConversationStoreOptions>? configure = null)
    {
        var options = new DaprConversationStoreOptions { StateStoreName = "statestore" };
        configure?.Invoke(options);
        options.Validate();
        return new DaprConversationStore(_backend, options, _clock) { Delay = (_, _) => Task.CompletedTask };
    }

    private static WorkflowChatMessage User(string text) => new() { Role = "user", Content = text };

    private static WorkflowChatMessage Assistant(string text) => new() { Role = "assistant", Content = text };

    private static ConversationTurn Turn(string turnId, long baseSequence, params WorkflowChatMessage[] messages) =>
        new(ConversationId, turnId, baseSequence, "planner", null, messages);

    private const string ManifestKey = "agent-conversation:" + ConversationId;

    private ConversationManifest Manifest() =>
        _backend.Raw(ManifestKey).Deserialize(ConversationStoreJsonContext.Default.ConversationManifest)!;

    private List<string> ChunkKeys() =>
        _backend.Keys.Where(k => k.StartsWith(ManifestKey + "#", StringComparison.Ordinal)).ToList();

    // ── Order, sequences and round-trip ────────────────────────────────────

    [Fact]
    public async Task Append_ThenLoad_ReturnsTheMessagesInOrderWithSequencesAndProvenance()
    {
        var store = CreateStore();

        var first = await store.AppendTurnAsync(Turn("t1", 0, User("hi"), Assistant("hello")), TestContext.Current.CancellationToken);
        _clock.Now = Start.AddMinutes(1);
        var second = await store.AppendTurnAsync(
            new ConversationTurn(ConversationId, "t2", 2, "critic", "key-a", [User("again"), Assistant("sure")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 0, 2, false), first);
        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 2, 4, false), second);

        var snapshot = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, snapshot.NextSequence);
        Assert.Equal(["hi", "hello", "again", "sure"], snapshot.Entries.Select(e => e.Message.Content));
        Assert.Equal([0L, 1, 2, 3], snapshot.Entries.Select(e => e.Sequence));
        Assert.Equal(["t1", "t1", "t2", "t2"], snapshot.Entries.Select(e => e.TurnId));
        Assert.Equal("critic", snapshot.Entries[2].AgentName);
        Assert.Equal("key-a", snapshot.Entries[2].ChatClientKey);
        Assert.Null(snapshot.Entries[0].ChatClientKey);
        Assert.Equal(Start, snapshot.Entries[0].CreatedAt);
        Assert.Equal(Start.AddMinutes(1), snapshot.Entries[3].CreatedAt);
    }

    [Fact]
    public async Task Append_KeepsEveryMessageField_IncludingReasoningAndItsSource()
    {
        var store = CreateStore();
        var message = new WorkflowChatMessage
        {
            Role = "assistant",
            Content = null,
            Reasoning = [new WorkflowReasoningContent { Text = "think", ProtectedData = "sig", Source = "planner" }],
            FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "search", ArgumentsJson = """{"q":"x"}""" }],
        };
        var result = new WorkflowChatMessage
        {
            Role = "tool",
            FunctionResults = [new WorkflowFunctionResult { CallId = "c1", Name = "search", ResultJson = "42" }],
        };

        await store.AppendTurnAsync(Turn("t1", 0, User("q"), message, result), TestContext.Current.CancellationToken);
        var entries = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries;

        var loaded = entries[1].Message;
        Assert.Null(loaded.Content);
        var reasoning = Assert.Single(loaded.Reasoning!);
        Assert.Equal(("think", "sig", "planner"), (reasoning.Text, reasoning.ProtectedData, reasoning.Source));
        var call = Assert.Single(loaded.FunctionCalls!);
        Assert.Equal(("c1", "search", """{"q":"x"}"""), (call.CallId, call.Name, call.ArgumentsJson));
        var stored = Assert.Single(entries[2].Message.FunctionResults!);
        Assert.Equal(("c1", "search", "42"), (stored.CallId, stored.Name, stored.ResultJson));
        Assert.Null(entries[0].Message.FunctionCalls);
        Assert.Null(entries[0].Message.Reasoning);
    }

    [Fact]
    public async Task StoredDocuments_UseSnakeCaseNamesAndOmitNulls()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(
            Turn("t1", 0, new WorkflowChatMessage
            {
                Role = "assistant",
                Content = "a",
                Reasoning = [new WorkflowReasoningContent { Text = "r", ProtectedData = "p" }],
            }),
            TestContext.Current.CancellationToken);

        var manifest = _backend.Raw(ManifestKey);
        Assert.Equal(
            ["version", "incarnation", "next_seq", "chunks", "turns", "mtime", "chunks_written_ms"],
            manifest.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["id", "first_seq", "count", "bytes"], manifest.GetProperty("chunks")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["id", "first_seq", "count", "base_seq", "base_moved"], manifest.GetProperty("turns")[0].EnumerateObject().Select(p => p.Name));

        var entry = _backend.Raw(Assert.Single(ChunkKeys())).GetProperty("entries")[0];
        Assert.Equal(["seq", "turn_id", "agent", "created_ms", "message"], entry.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["role", "content", "reasoning"], entry.GetProperty("message").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["text", "protected_data"], entry.GetProperty("message").GetProperty("reasoning")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task ManifestBytes_MatchTheStoredChunk()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("hi"), Assistant("hello")), TestContext.Current.CancellationToken);

        var descriptor = Assert.Single(Manifest().Chunks);
        var raw = _backend.Raw(ChunkKeys().Single());
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(raw).Length, descriptor.Bytes);
    }

    // ── Loading ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_MissingConversation_IsEmptyUnlessAPrefixIsRequested()
    {
        var store = CreateStore();

        var empty = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, empty.NextSequence);
        Assert.Empty(empty.Entries);
        Assert.Empty((await store.LoadAsync(ConversationId, 0, cancellationToken: TestContext.Current.CancellationToken)).Entries);

        var ex = await Assert.ThrowsAsync<ConversationNotFoundException>(
            () => store.LoadAsync(ConversationId, 3, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ConversationId, ex.ConversationId);
    }

    [Fact]
    public async Task Load_PrefixLongerThanTheConversation_IsNotFound()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("hi")), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConversationNotFoundException>(
            () => store.LoadAsync(ConversationId, 2, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PinnedLoad_ReturnsOnlyThePrefixAndReadsOnlyTheChunksItNeeds()
    {
        // Every entry is larger than half a chunk, so each append keeps its own chunk.
        var store = CreateStore(o => o.MaxChunkBytes = 200);
        var big = new string('x', 150);
        await store.AppendTurnAsync(Turn("t1", 0, User(big + "1")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 1, User(big + "2")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t3", 2, User(big + "3")), TestContext.Current.CancellationToken);
        Assert.Equal(3, Manifest().Chunks.Count);

        IReadOnlyList<string>? requested = null;
        _backend.BeforeGetBulk = keys => requested = keys;
        var snapshot = await store.LoadAsync(ConversationId, 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.NextSequence);
        Assert.Equal([big + "1", big + "2"], snapshot.Entries.Select(e => e.Message.Content));
        Assert.Equal(2, requested!.Count);
    }

    [Fact]
    public async Task PinnedLoad_IsUnchangedByLaterAppends()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        var before = await store.LoadAsync(ConversationId, 2, cancellationToken: TestContext.Current.CancellationToken);

        await store.AppendTurnAsync(Turn("t2", 2, User("c"), Assistant("d")), TestContext.Current.CancellationToken);
        var after = await store.LoadAsync(ConversationId, 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before.Entries.Select(e => e.Message.Content), after.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task Load_ChunkFoldedAway_ReadsTheManifestAgain()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);

        // The first bulk read races a concurrent append that folds the chunk and deletes it.
        var raced = false;
        _backend.BeforeGetBulk = _ =>
        {
            if (raced)
            {
                return;
            }

            raced = true;
            _backend.BeforeGetBulk = null;
            store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        };

        var snapshot = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(raced);
        Assert.Equal(["a", "b"], snapshot.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task Load_ChunkMissingEveryTime_GivesUp()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        _backend.Remove(ChunkKeys().Single());

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsNotType<ConversationNotFoundException>(ex);
        Assert.Equal(3, _backend.Log.Count(l => l.StartsWith("getbulk:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Load_GapInTheEntries_IsReportedAsCorruption()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);

        var chunkKey = ChunkKeys().Single();
        var chunk = _backend.Raw(chunkKey).Deserialize(ConversationStoreJsonContext.Default.ConversationChunk)!;
        chunk.Entries.RemoveAt(0);
        _backend.Put(chunkKey, JsonSerializer.SerializeToElement(chunk, ConversationStoreJsonContext.Default.ConversationChunk));

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("corrupt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ManifestFromANewerFormat_Throws()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var manifest = Manifest();
        manifest.Version = 2;
        _backend.Put(ManifestKey, JsonSerializer.SerializeToElement(manifest, ConversationStoreJsonContext.Default.ConversationManifest));

        await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoreWithoutETags_IsRejectedWithAClearError()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        _backend.OmitETags = true;

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken));
        Assert.Contains("ETag", ex.Message, StringComparison.Ordinal);
    }

    // ── Chunking ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Append_SplitsEntriesAcrossChunksBySize_AndAnOversizedEntryGetsItsOwn()
    {
        var store = CreateStore(o => o.MaxChunkBytes = 400);
        var messages = Enumerable.Range(0, 6).Select(i => User(new string('a', 60) + i)).ToList();
        messages.Insert(3, User(new string('z', 1000)));

        await store.AppendTurnAsync(Turn("t1", 0, [.. messages]), TestContext.Current.CancellationToken);

        var chunks = Manifest().Chunks;
        Assert.True(chunks.Count >= 3);
        Assert.Contains(chunks, c => c.Count == 1 && c.Bytes > 400);
        Assert.All(chunks.Where(c => c.Count > 1), c => Assert.True(c.Bytes <= 400, $"chunk of {c.Bytes} bytes"));
        Assert.Equal(Enumerable.Range(0, chunks.Count).Select(i => chunks.Take(i).Sum(c => (long)c.Count)), chunks.Select(c => c.FirstSequence));

        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(messages.Select(m => m.Content), loaded.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task Append_FoldsASmallLastChunk_AndDeletesTheOldOne()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var firstChunk = ChunkKeys().Single();

        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);

        var chunk = Assert.Single(Manifest().Chunks);
        Assert.Equal((0L, 2), (chunk.FirstSequence, chunk.Count));
        Assert.DoesNotContain(firstChunk, _backend.Keys);
        Assert.Single(ChunkKeys());
    }

    [Fact]
    public async Task Append_KeepsALastChunkThatIsAtLeastHalfFull()
    {
        var store = CreateStore(o => o.MaxChunkBytes = 300);
        await store.AppendTurnAsync(Turn("t1", 0, User(new string('x', 200))), TestContext.Current.CancellationToken);
        var firstChunk = ChunkKeys().Single();

        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);

        Assert.Equal(2, Manifest().Chunks.Count);
        Assert.Contains(firstChunk, _backend.Keys);
    }

    // ── TTL ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ttl_ManifestGetsTheTtlAndChunksTwice_OnEveryCommit()
    {
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(90.2));
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);

        Assert.Equal(91, _backend.TtlOf(ManifestKey));
        Assert.Equal(182, _backend.TtlOf(ChunkKeys().Single()));
        Assert.Equal(182, Manifest().ChunkTtlSeconds);

        // The second commit takes the ETag-conditioned path.
        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);

        Assert.Equal(91, _backend.TtlOf(ManifestKey));
        Assert.All(ChunkKeys(), k => Assert.Equal(182, _backend.TtlOf(k)));
    }

    [Fact]
    public async Task Ttl_KeptChunksAreRewrittenOnceHalfTheTtlHasPassed()
    {
        var store = CreateStore(o =>
        {
            o.TimeToLive = TimeSpan.FromSeconds(100);
            o.MaxChunkBytes = 300;
        });
        await store.AppendTurnAsync(Turn("t1", 0, User(new string('x', 200))), TestContext.Current.CancellationToken);
        var kept = ChunkKeys().Single();

        _clock.Now = Start.AddSeconds(40);
        await store.AppendTurnAsync(Turn("t2", 1, User(new string('y', 200))), TestContext.Current.CancellationToken);
        Assert.Equal(1, _backend.Log.Count(l => l == $"save:{kept}"));
        Assert.Equal(Start.ToUnixTimeMilliseconds(), Manifest().ChunksWrittenMs);

        _clock.Now = Start.AddSeconds(50);
        await store.AppendTurnAsync(Turn("t3", 2, User(new string('z', 200))), TestContext.Current.CancellationToken);

        Assert.Equal(2, _backend.Log.Count(l => l == $"save:{kept}"));
        Assert.Equal(200, _backend.TtlOf(kept));
        Assert.Equal(100, _backend.TtlOf(ManifestKey));
        Assert.Equal(Start.AddSeconds(50).ToUnixTimeMilliseconds(), Manifest().ChunksWrittenMs);
        Assert.Equal(3, (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries.Count);
    }

    [Fact]
    public async Task Ttl_ALoadAfterHalfTheTtl_RenewsTheManifestAndItsChunks()
    {
        // Review regression: only appends renewed the TTL, so a run paused longer than it lost its conversation.
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("hi"), Assistant("hello")), TestContext.Current.CancellationToken);
        var chunk = ChunkKeys().Single();
        _backend.Log.Clear();

        _clock.Now = Start.AddSeconds(60);
        var snapshot = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Contains($"save:{chunk}", _backend.Log);
        Assert.Contains($"trysave:{ManifestKey}", _backend.Log);
        Assert.Equal(100, _backend.TtlOf(ManifestKey));
        Assert.Equal(200, _backend.TtlOf(chunk));
        Assert.Equal(Start.AddSeconds(60).ToUnixTimeMilliseconds(), Manifest().ModifiedMs);
        Assert.Equal(Start.AddSeconds(60).ToUnixTimeMilliseconds(), Manifest().ChunksWrittenMs);

        // The next append still lands on the renewed manifest.
        var appended = await store.AppendTurnAsync(Turn("t2", 2, User("again")), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.Appended, appended.Status);
        Assert.Equal(3, (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries.Count);
    }

    [Fact]
    public async Task Ttl_ALoadCloseToExpiry_DoesNotRenew()
    {
        // Within the last quarter of the TTL a renewal could reach an expired key, which Redis would accept.
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("hi")), TestContext.Current.CancellationToken);
        _backend.Log.Clear();

        _clock.Now = Start.AddSeconds(80);
        await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_backend.Log, l => l.StartsWith("save:", StringComparison.Ordinal) || l.StartsWith("trysave", StringComparison.Ordinal));
        Assert.Equal(Start.ToUnixTimeMilliseconds(), Manifest().ModifiedMs);
    }

    [Fact]
    public async Task Ttl_ARenewalWhoseChunkWritesRanIntoTheMargin_DoesNotWriteTheManifest()
    {
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("hi")), TestContext.Current.CancellationToken);
        _backend.Log.Clear();
        _clock.Now = Start.AddSeconds(60);

        // The chunk rewrite is slow: by the time it is done, the manifest is in its last quarter.
        _backend.FailSave = _ =>
        {
            _clock.Now = Start.AddSeconds(90);
            return false;
        };
        await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_backend.Log, l => l.StartsWith("trysave", StringComparison.Ordinal));
        Assert.Equal(Start.ToUnixTimeMilliseconds(), Manifest().ModifiedMs);
    }

    [Fact]
    public async Task Ttl_ARenewalRacingADelete_DoesNotLeaveDeletedChunksBehind()
    {
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("secret")), TestContext.Current.CancellationToken);
        _clock.Now = Start.AddSeconds(60);

        // The delete lands after the renewal read the chunks and before it writes them back.
        var deleted = false;
        _backend.FailSave = _ =>
        {
            if (!deleted)
            {
                deleted = true;
                Assert.True(store.DeleteAsync(ConversationId).GetAwaiter().GetResult());
            }

            return false;
        };
        await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Empty(ChunkKeys());
        Assert.True(Manifest().Deleted);
    }

    [Fact]
    public async Task Ttl_ALoadBeforeHalfTheTtl_WritesNothing()
    {
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("hi")), TestContext.Current.CancellationToken);
        _backend.Log.Clear();

        _clock.Now = Start.AddSeconds(49);
        await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_backend.Log, l => l.StartsWith("save:", StringComparison.Ordinal) || l.StartsWith("trysave:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ttl_ALoadWithoutATtl_WritesNothing()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("hi")), TestContext.Current.CancellationToken);
        _backend.Log.Clear();

        _clock.Now = Start.AddDays(30);
        await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_backend.Log, l => l.StartsWith("save:", StringComparison.Ordinal) || l.StartsWith("trysave:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ttl_ARenewalThatFails_LeavesTheLoadAsItWas()
    {
        var store = CreateStore(o => o.TimeToLive = TimeSpan.FromSeconds(100));
        await store.AppendTurnAsync(Turn("t1", 0, User("hi"), Assistant("hello")), TestContext.Current.CancellationToken);
        _backend.FailSave = k => k.Contains('#');

        _clock.Now = Start.AddSeconds(80);
        var snapshot = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["hi", "hello"], snapshot.Entries.Select(e => e.Message.Content));
        Assert.Equal(Start.ToUnixTimeMilliseconds(), Manifest().ModifiedMs);
    }

    [Fact]
    public async Task Ttl_ChangedBetweenDeployments_RewritesTheKeptChunksAtOnce()
    {
        var first = CreateStore(o =>
        {
            o.TimeToLive = TimeSpan.FromSeconds(60);
            o.MaxChunkBytes = 300;
        });
        await first.AppendTurnAsync(Turn("t1", 0, User(new string('x', 200))), TestContext.Current.CancellationToken);
        var kept = ChunkKeys().Single();
        Assert.Equal(120, _backend.TtlOf(kept));

        var second = CreateStore(o =>
        {
            o.TimeToLive = TimeSpan.FromHours(1);
            o.MaxChunkBytes = 300;
        });
        _clock.Now = Start.AddSeconds(1);
        await second.AppendTurnAsync(Turn("t2", 1, User(new string('y', 200))), TestContext.Current.CancellationToken);

        Assert.Equal(7200, _backend.TtlOf(kept));
        Assert.Equal(3600, _backend.TtlOf(ManifestKey));
    }

    [Fact]
    public async Task Ttl_TurnedOff_RemovesTheExpiryOnce_AndThenSendsNoTtl()
    {
        var withTtl = CreateStore(o =>
        {
            o.TimeToLive = TimeSpan.FromSeconds(60);
            o.MaxChunkBytes = 300;
        });
        await withTtl.AppendTurnAsync(Turn("t1", 0, User(new string('x', 200))), TestContext.Current.CancellationToken);
        var kept = ChunkKeys().Single();

        var withoutTtl = CreateStore(o => o.MaxChunkBytes = 300);
        await withoutTtl.AppendTurnAsync(Turn("t2", 1, User(new string('y', 200))), TestContext.Current.CancellationToken);

        Assert.Equal(-1, _backend.TtlOf(kept));
        Assert.Equal(-1, _backend.TtlOf(ManifestKey));
        Assert.Null(Manifest().ChunkTtlSeconds);

        await withoutTtl.AppendTurnAsync(Turn("t3", 2, User(new string('z', 200))), TestContext.Current.CancellationToken);
        Assert.Null(_backend.TtlOf(ManifestKey));
    }

    [Fact]
    public async Task NoTtl_NeverSendsTtlMetadata()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);

        Assert.All(_backend.Keys, k => Assert.Null(_backend.TtlOf(k)));
    }

    // ── Concurrency ────────────────────────────────────────────────────────

    [Fact]
    public async Task EtagConflict_RetriesOnTheNewManifest_AndBothTurnsAreStored()
    {
        // Entries larger than half a chunk, so no append folds (and deletes) a chunk the other one reads.
        var store = CreateStore(o => o.MaxChunkBytes = 200);
        var big = new string('x', 150);
        await store.AppendTurnAsync(Turn("t0", 0, User(big)), TestContext.Current.CancellationToken);

        // Right after the append below reads the manifest, another writer commits a turn.
        var injected = false;
        _backend.AfterGet = async _ =>
        {
            if (injected)
            {
                return;
            }

            injected = true;
            await store.AppendTurnAsync(Turn("other", 1, User(big)), TestContext.Current.CancellationToken);
        };

        var result = await store.AppendTurnAsync(Turn("mine", 1, User(big)), TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 2, 3, true), result);
        Assert.Contains(_backend.Log, l => l.StartsWith("trysave-rejected:", StringComparison.Ordinal));
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["t0", "other", "mine"], loaded.Entries.Select(e => e.TurnId));

        // The losing attempt's chunks were deleted: only the referenced chunk is left.
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task TwoFirstAppends_OneCreatesTheManifest_AndTheOtherRetries()
    {
        var store = CreateStore();
        var injected = false;
        _backend.AfterGet = async _ =>
        {
            if (injected)
            {
                return;
            }

            injected = true;
            await store.AppendTurnAsync(Turn("other", 0, User("concurrent")), TestContext.Current.CancellationToken);
        };

        var result = await store.AppendTurnAsync(Turn("mine", 0, User("mine")), TestContext.Current.CancellationToken);

        Assert.True(result.BaseMoved);
        Assert.Contains(_backend.Log, l => l.StartsWith("trycreate-rejected:", StringComparison.Ordinal));
        Assert.Equal(2, (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).NextSequence);
        Assert.Single(ChunkKeys());
    }

    [Fact]
    public async Task PersistentConflict_GivesUpAfterMaxAttempts()
    {
        // Entries larger than half a chunk, so every attempt reaches its commit and loses the ETag race there.
        var store = CreateStore(o =>
        {
            o.MaxCommitAttempts = 3;
            o.MaxChunkBytes = 200;
        });
        var big = new string('x', 150);
        await store.AppendTurnAsync(Turn("t0", 0, User(big)), TestContext.Current.CancellationToken);

        // Another writer always commits between this append's read and its commit.
        var n = 0;
        async Task Interfere(string key)
        {
            _backend.AfterGet = null;
            try
            {
                await store.AppendTurnAsync(Turn($"other-{n++}", 0, User(big)), TestContext.Current.CancellationToken);
            }
            finally
            {
                _backend.AfterGet = Interfere;
            }
        }

        _backend.AfterGet = Interfere;

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.AppendTurnAsync(Turn("mine", 1, User(big)), TestContext.Current.CancellationToken));

        Assert.Contains("3 attempts", ex.Message, StringComparison.Ordinal);
        Assert.Equal(3, _backend.Log.Count(l => l.StartsWith("trysave-rejected:", StringComparison.Ordinal)));
        _backend.AfterGet = null;
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(loaded.Entries, e => e.TurnId == "mine");
    }

    [Fact]
    public async Task BaseMoved_IsReportedWhenTheTurnWasProducedAgainstAnOlderBase()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);

        var result = await store.AppendTurnAsync(Turn("t2", 0, User("c")), TestContext.Current.CancellationToken);

        Assert.True(result.BaseMoved);
        Assert.Equal(2, result.FirstSequence);
    }

    // ── Idempotency and ambiguous outcomes ─────────────────────────────────

    [Fact]
    public async Task SameTurnId_IsAppendedOnce_AndReturnsTheOriginalRange()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 2, User("c")), TestContext.Current.CancellationToken);

        var again = await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 0, 3, false), again);
        Assert.Equal(3, (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).NextSequence);
    }

    [Fact]
    public async Task LostReply_TheCommitLandedButThrew_IsAlreadyAppendedWithItsChunksIntact()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("first")), TestContext.Current.CancellationToken);

        var faulted = false;
        _backend.CommitFaults = _ =>
        {
            if (faulted)
            {
                return CommitFault.None;
            }

            faulted = true;
            return CommitFault.LandThenThrow;
        };

        var result = await store.AppendTurnAsync(Turn("t1", 1, User("a"), Assistant("b")), TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 1, 3, false), result);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["first", "a", "b"], loaded.Entries.Select(e => e.Message.Content));
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task LateLanding_IsRejected_AndOnlyItsUnreferencedChunksAreDeleted()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("first")), TestContext.Current.CancellationToken);
        var etagBefore = _backend.ETagOf(ManifestKey);

        var faulted = false;
        _backend.CommitFaults = _ =>
        {
            if (faulted)
            {
                return CommitFault.None;
            }

            faulted = true;
            return CommitFault.ThrowAndHold;
        };

        var result = await store.AppendTurnAsync(Turn("t1", 1, User("a")), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.Appended, result.Status);
        Assert.NotEqual(etagBefore, _backend.ETagOf(ManifestKey));

        // The delayed first attempt arrives after the second one committed: it is rejected.
        Assert.Equal(0, _backend.DeliverHeldWrites());

        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["first", "a"], loaded.Entries.Select(e => e.Message.Content));
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task DeletionRule_NoChunkIsDeletedWhileTheUnknownCommitCouldStillLand()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("first")), TestContext.Current.CancellationToken);

        // Attempt 1's commit is held; attempt 2 reads the same ETag, so attempt 1 could still land.
        var attempts = 0;
        _backend.CommitFaults = _ => ++attempts == 1 ? CommitFault.ThrowAndHold : CommitFault.None;
        List<string>? chunksAtSecondRead = null;
        var reads = 0;
        _backend.AfterGet = _ =>
        {
            if (++reads == 2)
            {
                chunksAtSecondRead = ChunkKeys();

                // Deliver attempt 1 now: it lands, because the ETag has not moved.
                Assert.Equal(1, _backend.DeliverHeldWrites());
            }

            return Task.CompletedTask;
        };

        var result = await store.AppendTurnAsync(Turn("t1", 1, User("a")), TestContext.Current.CancellationToken);

        // Attempt 2 then lost the ETag race to attempt 1's late write, and found the turn on its next read.
        Assert.Equal(ConversationAppendStatus.AlreadyAppended, result.Status);
        Assert.NotNull(chunksAtSecondRead);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["first", "a"], loaded.Entries.Select(e => e.Message.Content));
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task UnknownOutcome_ThatNeverLands_IsRetriedAndCleanedUp()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("first")), TestContext.Current.CancellationToken);
        var attempts = 0;
        _backend.CommitFaults = _ => ++attempts == 1 ? CommitFault.ThrowAndDrop : CommitFault.None;

        var result = await store.AppendTurnAsync(Turn("t1", 1, User("a")), TestContext.Current.CancellationToken);

        Assert.Equal(ConversationAppendStatus.Appended, result.Status);
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task ChunkWriteFailure_DeletesWhatWasWritten_AndLeavesTheConversationUnchanged()
    {
        var store = CreateStore(o => o.MaxChunkBytes = 200);
        await store.AppendTurnAsync(Turn("t0", 0, User(new string('x', 150))), TestContext.Current.CancellationToken);
        var before = _backend.Keys.Order().ToList();

        var saves = 0;
        _backend.FailSave = key => key.Contains('#') && ++saves == 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendTurnAsync(
            Turn("t1", 1, User(new string('a', 150)), User(new string('b', 150))), TestContext.Current.CancellationToken));

        Assert.Equal(before, _backend.Keys.Order().ToList());
    }

    [Fact]
    public async Task DedupeWindow_KeepsOnlyTheMostRecentTurns()
    {
        var store = CreateStore(o => o.TurnDedupeWindow = DaprConversationStoreOptions.MinTurnDedupeWindow);
        for (var i = 0; i <= DaprConversationStoreOptions.MinTurnDedupeWindow; i++)
        {
            await store.AppendTurnAsync(Turn($"t{i}", i, User($"m{i}")), TestContext.Current.CancellationToken);
        }

        Assert.Equal(DaprConversationStoreOptions.MinTurnDedupeWindow, Manifest().Turns.Count);
        Assert.Equal("t1", Manifest().Turns[0].Id);

        // t0 fell out of the window, so appending it again is a new turn: the documented bound.
        var again = await store.AppendTurnAsync(Turn("t0", 0, User("m0")), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.Appended, again.Status);
        var recent = await store.AppendTurnAsync(Turn("t5", 5, User("m5")), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.AlreadyAppended, recent.Status);
    }

    [Fact]
    public async Task ModifiedTime_AlwaysIncreases()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var first = Manifest().ModifiedMs;

        _clock.Now = Start.AddSeconds(-30);
        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);

        Assert.Equal(first + 1, Manifest().ModifiedMs);
    }

    // ── Keys, delete and validation ────────────────────────────────────────

    [Fact]
    public async Task Keys_UseThePrefix_AndEscapeTheDaprSeparator()
    {
        var store = CreateStore(o => o.KeyPrefix = "p:");
        await store.AppendTurnAsync(
            new ConversationTurn("a||b", "t1", 0, "planner", null, [User("x")]), TestContext.Current.CancellationToken);

        Assert.Contains("p:a%7C%7Cb", _backend.Keys);
        Assert.All(_backend.Keys, k => Assert.DoesNotContain("|", k, StringComparison.Ordinal));
        Assert.Single((await store.LoadAsync("a||b", cancellationToken: TestContext.Current.CancellationToken)).Entries);
    }

    [Fact]
    public async Task Delete_RemovesTheManifestAndChunks_AndReportsWhetherItExisted()
    {
        var store = CreateStore(o => o.MaxChunkBytes = 200);
        await store.AppendTurnAsync(Turn("t1", 0, User(new string('x', 150)), User(new string('y', 150))), TestContext.Current.CancellationToken);
        Assert.Equal(2, ChunkKeys().Count);

        Assert.True(await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken));
        Assert.Equal([ManifestKey], _backend.Keys);
        Assert.True(Manifest().Deleted);
        Assert.False(await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken));

        Assert.Empty((await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries);
        await Assert.ThrowsAsync<ConversationNotFoundException>(
            () => store.LoadAsync(ConversationId, 1, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task InvalidConversationIds_AreRejected(string id)
    {
        var store = CreateStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.DeleteAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TooLongConversationId_IsRejected()
    {
        var store = CreateStore();
        var id = new string('a', DaprConversationStoreOptions.MaxConversationIdLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidTurns_AreRejectedBeforeAnyWrite()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendTurnAsync(Turn("t1", 0), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendTurnAsync(Turn(" ", 0, User("a")), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendTurnAsync(Turn("t1", -1, User("a")), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendTurnAsync(
            new ConversationTurn(ConversationId, "t1", 0, "", null, [User("a")]), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync(ConversationId, -1, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(_backend.Keys);
    }

    [Theory]
    [InlineData(nameof(DaprConversationStoreOptions.StateStoreName))]
    [InlineData(nameof(DaprConversationStoreOptions.MaxChunkBytes))]
    [InlineData(nameof(DaprConversationStoreOptions.TurnDedupeWindow))]
    [InlineData(nameof(DaprConversationStoreOptions.MaxCommitAttempts))]
    [InlineData(nameof(DaprConversationStoreOptions.TimeToLive))]
    public void Validate_RejectsEachOutOfRangeSetting(string setting)
    {
        var options = new DaprConversationStoreOptions { StateStoreName = "statestore" };
        switch (setting)
        {
            case nameof(DaprConversationStoreOptions.StateStoreName): options.StateStoreName = " "; break;
            case nameof(DaprConversationStoreOptions.MaxChunkBytes): options.MaxChunkBytes = 0; break;
            case nameof(DaprConversationStoreOptions.TurnDedupeWindow): options.TurnDedupeWindow = 15; break;
            case nameof(DaprConversationStoreOptions.MaxCommitAttempts): options.MaxCommitAttempts = 0; break;
            case nameof(DaprConversationStoreOptions.TimeToLive): options.TimeToLive = TimeSpan.FromMilliseconds(999); break;
        }

        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal(setting, ex.ParamName);
    }

    // ── Review regressions: deletes, incarnations, key collisions ──────────

    [Fact]
    public async Task DeleteDuringAnAppend_LeavesNoManifestPointingAtDeletedChunks()
    {
        // A full first chunk, which the append keeps, and a small tail it folds.
        var store = CreateStore(o => o.MaxChunkBytes = 300);
        await store.AppendTurnAsync(Turn("t1", 0, User(new string('x', 200))), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 1, User("small")), TestContext.Current.CancellationToken);

        // The delete runs after the append read the manifest and wrote its chunks, before its commit.
        var deleted = false;
        _backend.BeforeCommit = async _ =>
        {
            if (deleted)
            {
                return;
            }

            deleted = true;
            _backend.BeforeCommit = null;
            Assert.True(await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken));
        };

        var result = await store.AppendTurnAsync(Turn("t3", 2, User("late")), TestContext.Current.CancellationToken);

        // The append lost to the tombstone and then started a new conversation with only its own turn.
        Assert.True(deleted);
        Assert.True(result.BaseMoved);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["late"], loaded.Entries.Select(e => e.Message.Content));
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
    }

    [Fact]
    public async Task UnknownCommit_WhileTheManifestIsAbsent_KeepsItsChunks()
    {
        // The manifest disappears (expired) while a commit conditioned on its ETag is held: Redis would
        // still accept that commit, so its chunks must not be deleted.
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("first")), TestContext.Current.CancellationToken);

        // Attempt 1's commit is held, and the manifest expires right then. Attempt 2 reads the empty key, and
        // the held commit lands just before attempt 2's own commit.
        var attempts = 0;
        _backend.CommitFaults = key =>
        {
            if (++attempts != 1)
            {
                return CommitFault.None;
            }

            _backend.Remove(key);
            return CommitFault.ThrowAndHold;
        };
        _backend.BeforeCommit = _ =>
        {
            if (attempts == 1)
            {
                _backend.BeforeCommit = null;
                Assert.Equal(1, _backend.DeliverHeldWrites());
            }

            return Task.CompletedTask;
        };

        var result = await store.AppendTurnAsync(Turn("t1", 1, User("a")), TestContext.Current.CancellationToken);

        Assert.Equal(ConversationAppendStatus.AlreadyAppended, result.Status);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["first", "a"], loaded.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task HeldFirstCreate_ItsChunksSurviveUntilItCannotLand()
    {
        var store = CreateStore();
        var attempts = 0;
        _backend.CommitFaults = _ => ++attempts == 1 ? CommitFault.ThrowAndHold : CommitFault.None;
        List<string>? chunksBeforeSecondCommit = null;
        _backend.BeforeCommit = _ =>
        {
            if (attempts == 1)
            {
                chunksBeforeSecondCommit = ChunkKeys();
            }

            return Task.CompletedTask;
        };

        var result = await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);

        Assert.Equal(ConversationAppendStatus.Appended, result.Status);
        // Attempt 2 read an absent key, so attempt 1's create could still land: both attempts' chunks existed.
        Assert.Equal(2, chunksBeforeSecondCommit!.Count);
        Assert.Equal(0, _backend.DeliverHeldWrites());
        Assert.Equal(Manifest().Chunks.Select(c => ManifestKey + "#" + c.Id).Order(), ChunkKeys().Order());
        Assert.Single((await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries);
    }

    [Fact]
    public async Task PinnedLoad_OfAConversationCreatedAgain_IsNotFound_EvenWhenTheNewOneIsLonger()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("old-a"), Assistant("old-b")), TestContext.Current.CancellationToken);
        var pinned = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(pinned.Incarnation);

        await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("n1", 0, User("new-a"), Assistant("new-b"), User("new-c")), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadAsync(
            ConversationId, pinned.NextSequence, pinned.Incarnation, TestContext.Current.CancellationToken));

        var current = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(pinned.Incarnation, current.Incarnation);
        Assert.Equal(["new-a", "new-b", "new-c"], current.Entries.Select(e => e.Message.Content));
        Assert.Equal(current.Entries.Count, (await store.LoadAsync(
            ConversationId, 3, current.Incarnation, TestContext.Current.CancellationToken)).Entries.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Append_ProducedFromHistoryThatWasDeleted_IsNotStored(bool recreated)
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var old = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken);
        if (recreated)
        {
            await store.AppendTurnAsync(Turn("t2", 0, User("b")), TestContext.Current.CancellationToken);
        }

        var before = _backend.Keys.Order().ToList();
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.AppendTurnAsync(
            Turn("t3", 1, User("c")) with { BaseIncarnation = old.Incarnation }, TestContext.Current.CancellationToken));

        Assert.Equal(before, _backend.Keys.Order().ToList());
    }

    [Fact]
    public async Task Append_WithTheCurrentIncarnation_IsStored()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var current = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        var result = await store.AppendTurnAsync(
            Turn("t2", 1, User("b")) with { BaseIncarnation = current.Incarnation }, TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 1, 2, false), result);
    }

    [Fact]
    public async Task ReAppend_OfATurnWhoseBaseMoved_ReportsItAgain()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        var first = await store.AppendTurnAsync(Turn("t2", 0, User("c")), TestContext.Current.CancellationToken);
        var logBefore = _backend.Log.Count;

        var again = await store.AppendTurnAsync(Turn("t2", 0, User("c")), TestContext.Current.CancellationToken);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 2, 3, true), first);
        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 2, 3, true), again);
        Assert.Equal(0, Manifest().Turns[^1].BaseSequence);
        Assert.All(_backend.Log.Skip(logBefore), l => Assert.StartsWith("get:", l, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_AfterItsTombstoneReplyWasLost_StillDeletesTheChunks()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var faulted = false;
        _backend.CommitFaults = _ =>
        {
            if (faulted)
            {
                return CommitFault.None;
            }

            faulted = true;
            return CommitFault.LandThenThrow;
        };

        Assert.True(await store.DeleteAsync(ConversationId, TestContext.Current.CancellationToken));
        Assert.Equal([ManifestKey], _backend.Keys);
    }

    [Theory]
    [InlineData("a||b", "a__b")]
    [InlineData("a|b", "a%7Cb")]
    [InlineData("x%23y", "x#y")]
    public async Task DistinctIds_NeverShareAConversation(string one, string other)
    {
        var store = CreateStore();
        await store.AppendTurnAsync(new ConversationTurn(one, "t1", 0, "planner", null, [User("one")]), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(new ConversationTurn(other, "t1", 0, "planner", null, [User("other")]), TestContext.Current.CancellationToken);

        Assert.Equal(["one"], (await store.LoadAsync(one, cancellationToken: TestContext.Current.CancellationToken)).Entries.Select(e => e.Message.Content));
        Assert.Equal(["other"], (await store.LoadAsync(other, cancellationToken: TestContext.Current.CancellationToken)).Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task AnIdNeverReachesAnotherConversationsChunks()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var chunkId = Manifest().Chunks.Single().Id;

        var probe = await store.LoadAsync(ConversationId + "#" + chunkId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(probe.Entries);
        Assert.Contains(ManifestKey + "#" + chunkId, _backend.Keys);
    }

    [Fact]
    public async Task Load_MissingLastEntry_IsReportedAsCorruption()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        RewriteChunk(chunk => chunk.Entries.RemoveAt(1));

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("corrupt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_DuplicateSequence_IsReportedAsCorruption()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        RewriteChunk(chunk => chunk.Entries[1].Sequence = 0);

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("corrupt", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a|b")]
    [InlineData("a#")]
    public void Validate_RejectsKeyPrefixesWithSeparators(string prefix)
    {
        var options = new DaprConversationStoreOptions { StateStoreName = "statestore", KeyPrefix = prefix };

        Assert.Equal(nameof(DaprConversationStoreOptions.KeyPrefix), Assert.Throws<ArgumentException>(options.Validate).ParamName);
    }

    // ── Summaries ──────────────────────────────────────────────────────────

    private static ConversationSummary Summary(string id, long covers, string text, string? incarnation) =>
        new(ConversationId, id, covers, "planner", null, new WorkflowChatMessage { Role = "system", Content = text }, incarnation);

    [Fact]
    public async Task Summary_ReplacesTheEntriesItCovers_InLaterLoads_AndKeepsThemStored()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 2, User("c"), Assistant("d")), TestContext.Current.CancellationToken);
        var before = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        var result = await store.AppendSummaryAsync(Summary("s1", 2, "S1", before.Incarnation), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t3", 5, User("e")), TestContext.Current.CancellationToken);

        Assert.Equal((ConversationAppendStatus.Appended, 4L, 5L), (result.Status, result.FirstSequence, result.NextSequence));
        var after = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(6, after.NextSequence);
        Assert.Equal(["S1", "c", "d", "e"], after.Entries.Select(e => e.Message.Content));
        Assert.Equal((4L, (long?)2), (after.Entries[0].Sequence, after.Entries[0].SummaryOf));
        Assert.All(after.Entries.Skip(1), e => Assert.Null(e.SummaryOf));

        // A prefix pinned before the summary is unchanged.
        var pinned = await store.LoadAsync(ConversationId, 4, before.Incarnation, TestContext.Current.CancellationToken);
        Assert.Equal(["a", "b", "c", "d"], pinned.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task ALaterSummary_SupersedesTheEarlierOne()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 1, User("b")), TestContext.Current.CancellationToken);
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s1", 1, "S1", inc), TestContext.Current.CancellationToken);   // seq 2
        await store.AppendTurnAsync(Turn("t3", 3, User("c")), TestContext.Current.CancellationToken);          // seq 3
        await store.AppendSummaryAsync(Summary("s2", 3, "S2", inc), TestContext.Current.CancellationToken);   // seq 4
        await store.AppendTurnAsync(Turn("t4", 5, User("d")), TestContext.Current.CancellationToken);          // seq 5

        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["S2", "c", "d"], loaded.Entries.Select(e => e.Message.Content));

        // A prefix that ends before the second summary uses the first one, without the second.
        var older = await store.LoadAsync(ConversationId, 4, inc, TestContext.Current.CancellationToken);
        Assert.Equal(["S1", "b", "c"], older.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task SummarizedLoad_ReadsOnlyTheChunksFromWhatTheSummaryCovers()
    {
        var store = CreateStore(o => o.MaxChunkBytes = 200);
        var big = new string('x', 150);
        for (var i = 0; i < 4; i++)
        {
            await store.AppendTurnAsync(Turn($"t{i}", i, User(big + i)), TestContext.Current.CancellationToken);
        }

        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s1", 3, "S", inc), TestContext.Current.CancellationToken);

        IReadOnlyList<string>? requested = null;
        _backend.BeforeGetBulk = keys => requested = keys;
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["S", big + 3], loaded.Entries.Select(e => e.Message.Content));
        Assert.Equal(2, requested!.Count);
    }

    [Fact]
    public async Task Summary_IsIdempotent_AndChecked()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;

        await store.AppendSummaryAsync(Summary("s1", 1, "S", inc), TestContext.Current.CancellationToken);
        var again = await store.AppendSummaryAsync(Summary("s1", 1, "S", inc), TestContext.Current.CancellationToken);
        Assert.Equal(ConversationAppendStatus.AlreadyAppended, again.Status);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendSummaryAsync(Summary("s2", 0, "S", inc), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendSummaryAsync(Summary("s3", 9, "S", inc), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.AppendSummaryAsync(Summary("s4", 1, "S", "other"), TestContext.Current.CancellationToken));
        Assert.Equal(3, (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).NextSequence);
    }

    [Fact]
    public async Task SummaryEntry_RoundTripsItsMarker()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a")), TestContext.Current.CancellationToken);
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s1", 1, "S", inc), TestContext.Current.CancellationToken);

        Assert.Equal([new SummaryRecord { Sequence = 1, CoversUpToSequence = 1 }.Sequence], Manifest().Summaries!.Select(r => r.Sequence));
        var entry = _backend.Raw(ChunkKeys().Single()).GetProperty("entries")[1];
        Assert.Equal(1, entry.GetProperty("summary_of").GetInt64());
    }

    [Fact]
    public async Task ASupersededSummaryInsideTheKeptRange_IsLeftOut()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);   // 0,1
        await store.AppendTurnAsync(Turn("t2", 2, User("c"), Assistant("d")), TestContext.Current.CancellationToken);   // 2,3
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s1", 2, "S1", inc), TestContext.Current.CancellationToken);             // 4
        await store.AppendTurnAsync(Turn("t3", 5, User("e")), TestContext.Current.CancellationToken);                   // 5
        await store.AppendSummaryAsync(Summary("s2", 2, "S2", inc), TestContext.Current.CancellationToken);             // 6, S1 is inside [2, 7)

        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["S2", "c", "d", "e"], loaded.Entries.Select(e => e.Message.Content));
    }

    [Fact]
    public async Task PinnedLoad_WhoseSummaryWasDropped_FailsInsteadOfChangingItsHistory()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t0", 0, User("a")), TestContext.Current.CancellationToken);
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s0", 1, "S0", inc), TestContext.Current.CancellationToken);              // seq 1
        await store.AppendTurnAsync(Turn("t1", 2, User("b")), TestContext.Current.CancellationToken);                     // seq 2
        var pinned = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["S0", "b"], pinned.Entries.Select(e => e.Message.Content));

        for (var i = 1; i <= 32; i++)
        {
            await store.AppendSummaryAsync(Summary($"s{i}", 1, $"S{i}", inc), TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<ConversationNotFoundException>(() =>
            store.LoadAsync(ConversationId, pinned.NextSequence, pinned.Incarnation, TestContext.Current.CancellationToken));
        Assert.Equal("S32", (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries[0].Message.Content);
    }

    [Fact]
    public async Task LoadEntries_ReturnsTheRawHistory_SummariesIncluded()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b")), TestContext.Current.CancellationToken);
        var inc = (await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Incarnation;
        await store.AppendSummaryAsync(Summary("s1", 2, "S", inc), TestContext.Current.CancellationToken);
        await store.AppendTurnAsync(Turn("t2", 3, User("c")), TestContext.Current.CancellationToken);

        var all = await store.LoadEntriesAsync(ConversationId, 0, cancellationToken: TestContext.Current.CancellationToken);
        var middle = await store.LoadEntriesAsync(ConversationId, 1, 3, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "S", "c"], all.Select(e => e.Message.Content));
        Assert.Equal([null, null, (long?)2, null], all.Select(e => e.SummaryOf));
        Assert.Equal(["b", "S"], middle.Select(e => e.Message.Content));
        Assert.Empty(await store.LoadEntriesAsync("missing", 0, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadEntries_GapInTheEntries_IsReportedAsCorruption_LikeLoad()
    {
        var store = CreateStore();
        await store.AppendTurnAsync(Turn("t1", 0, User("a"), Assistant("b"), User("c")), TestContext.Current.CancellationToken);
        RewriteChunk(chunk => chunk.Entries.RemoveAt(1));

        var ex = await Assert.ThrowsAsync<ConversationStoreException>(
            () => store.LoadEntriesAsync(ConversationId, 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("corrupt", ex.Message, StringComparison.Ordinal);
    }

    private void RewriteChunk(Action<ConversationChunk> change)
    {
        var chunkKey = ChunkKeys().Single();
        var chunk = _backend.Raw(chunkKey).Deserialize(ConversationStoreJsonContext.Default.ConversationChunk)!;
        change(chunk);
        _backend.Put(chunkKey, JsonSerializer.SerializeToElement(chunk, ConversationStoreJsonContext.Default.ConversationChunk));
    }
}
