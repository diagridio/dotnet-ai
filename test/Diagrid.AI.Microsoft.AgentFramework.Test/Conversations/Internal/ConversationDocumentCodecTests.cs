// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations.Internal;

public sealed class ConversationDocumentCodecTests
{
    [Fact]
    public void Serialize_WritesTheStoredManifestShape()
    {
        var manifest = new ConversationManifest
        {
            Incarnation = "i",
            NextSequence = 2,
            Chunks = [new() { Id = "c", FirstSequence = 0, Count = 2, Bytes = 40 }],
            ModifiedMs = 5,
            ChunksWrittenMs = 4,
        };

        Assert.Equal(
            """{"version":1,"incarnation":"i","next_seq":2,"chunks":[{"id":"c","first_seq":0,"count":2,"bytes":40}],"turns":[],"mtime":5,"chunks_written_ms":4}""",
            ConversationDocumentCodec.Serialize(manifest).GetRawText());
    }

    [Fact]
    public void ReadManifest_RejectsANewerFormat_AndAnInvalidDocument()
    {
        var newer = ConversationDocumentCodec.Serialize(new ConversationManifest { Version = ConversationManifest.CurrentVersion + 1 });
        Assert.Contains("newer version", Assert.Throws<ConversationStoreException>(
            () => ConversationDocumentCodec.ReadManifest(newer, "k")).Message);

        Assert.Throws<ConversationStoreException>(
            () => ConversationDocumentCodec.ReadManifest(JsonDocument.Parse("null").RootElement, "k"));
        Assert.IsType<JsonException>(Assert.Throws<ConversationStoreException>(
            () => ConversationDocumentCodec.ReadManifest(JsonDocument.Parse("""{"next_seq":"x"}""").RootElement, "k")).InnerException);
    }

    [Fact]
    public void Clone_IsADeepCopy()
    {
        var manifest = new ConversationManifest { Chunks = [new() { Id = "c" }], ModifiedMs = 1 };

        var clone = ConversationDocumentCodec.Clone(manifest);
        clone.ModifiedMs = 2;
        clone.Chunks[0].Id = "d";

        Assert.Equal(1, manifest.ModifiedMs);
        Assert.Equal("c", manifest.Chunks[0].Id);
    }

    [Fact]
    public void CreateChunk_DescribesTheDocument_AndReadChunkReadsItBack()
    {
        var turn = new ConversationTurn("conv", "t1", 0, "agent", "key",
            [new WorkflowChatMessage { Role = "user", Content = "hi" }, new WorkflowChatMessage { Role = "assistant", Content = "yo" }]);
        var entries = ConversationDocumentCodec.ToStoredEntries(turn, 7, 1000, summaryOf: null);

        var (descriptor, document) = ConversationDocumentCodec.CreateChunk(entries, "c1");

        Assert.Equal("c1", descriptor.Id);
        Assert.Equal(7, descriptor.FirstSequence);
        Assert.Equal(2, descriptor.Count);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(document.GetRawText()), descriptor.Bytes);

        var read = ConversationDocumentCodec.ReadChunk(document, "k").Entries.Select(ConversationDocumentCodec.ToEntry).ToList();
        Assert.Equal([7L, 8], read.Select(e => e.Sequence));
        Assert.Equal(["hi", "yo"], read.Select(e => e.Message.Content));
        Assert.All(read, e => Assert.Equal(("t1", "agent", "key"), (e.TurnId, e.AgentName, e.ChatClientKey)));
        Assert.All(read, e => Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), e.CreatedAt));
    }

    [Fact]
    public void ReadChunk_RejectsAnInvalidDocument() =>
        Assert.Throws<ConversationStoreException>(
            () => ConversationDocumentCodec.ReadChunk(JsonDocument.Parse("null").RootElement, "k"));
}
