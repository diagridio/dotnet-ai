// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations.Internal;

public sealed class ChunkPlannerTests
{
    private static StoredConversationEntry Entry(long sequence, string content = "x") => new()
    {
        Sequence = sequence,
        TurnId = "t",
        AgentName = "a",
        Message = new StoredChatMessage { Role = "user", Content = content },
    };

    private static ChunkDescriptor Chunk(int bytes) => new() { Id = "c", Bytes = bytes };

    [Fact]
    public void ShouldFoldTail_OnlyWhenTheLastChunkIsLessThanHalfFull()
    {
        Assert.False(ChunkPlanner.ShouldFoldTail([], 100));
        Assert.True(ChunkPlanner.ShouldFoldTail([Chunk(100), Chunk(49)], 100));
        Assert.False(ChunkPlanner.ShouldFoldTail([Chunk(10), Chunk(50)], 100));
    }

    [Fact]
    public void EnvelopeBytes_MatchTheChunkDocumentTheCodecWrites()
    {
        List<StoredConversationEntry> entries = [Entry(0), Entry(1), Entry(2)];

        var (descriptor, _) = ConversationDocumentCodec.CreateChunk(entries, "c");

        var planned = ChunkPlanner.ChunkEnvelopeBytes + entries.Sum(ConversationDocumentCodec.EntryBytes) + (entries.Count - 1);
        Assert.Equal(descriptor.Bytes, planned);
    }

    [Fact]
    public void Pack_KeepsEverythingInOneChunk_WhenItFits()
    {
        List<StoredConversationEntry> entries = [Entry(0), Entry(1), Entry(2)];

        var chunks = ChunkPlanner.Pack(entries, 1024 * 1024);

        Assert.Equal([[0L, 1, 2]], chunks.Select(c => c.Select(e => e.Sequence).ToArray()));
    }

    [Fact]
    public void Pack_StartsANewChunkAtTheExactByteLimit()
    {
        List<StoredConversationEntry> entries = [Entry(0), Entry(1), Entry(2)];
        var two = ConversationDocumentCodec.CreateChunk(entries.Take(2).ToList(), "c").Descriptor.Bytes;

        var exact = ChunkPlanner.Pack(entries, two);
        var oneLess = ChunkPlanner.Pack(entries, two - 1);

        Assert.Equal([[0L, 1], [2L]], exact.Select(c => c.Select(e => e.Sequence).ToArray()));
        Assert.Equal([[0L], [1L], [2L]], oneLess.Select(c => c.Select(e => e.Sequence).ToArray()));
    }

    [Fact]
    public void Pack_GivesAnOversizedEntryAChunkOfItsOwn()
    {
        List<StoredConversationEntry> entries = [Entry(0), Entry(1, new string('y', 500)), Entry(2)];

        var chunks = ChunkPlanner.Pack(entries, 200);

        Assert.Equal([[0L], [1L], [2L]], chunks.Select(c => c.Select(e => e.Sequence).ToArray()));
    }

    [Fact]
    public void Pack_ReturnsNoChunks_ForNoEntries() =>
        Assert.Empty(ChunkPlanner.Pack([], 100));
}
