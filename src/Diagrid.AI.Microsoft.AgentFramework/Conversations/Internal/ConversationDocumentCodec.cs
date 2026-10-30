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

using System.Text.Json;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

/// <summary>
/// Turns the stored documents into <see cref="JsonElement"/>s and back, always through
/// <see cref="ConversationStoreJsonContext"/>, and maps entries between their stored and public shapes.
/// </summary>
internal static class ConversationDocumentCodec
{
    /// <summary>Serializes a manifest to the document written at its key.</summary>
    public static JsonElement Serialize(ConversationManifest manifest) =>
        JsonSerializer.SerializeToElement(manifest, ConversationStoreJsonContext.Default.ConversationManifest);

    /// <summary>
    /// Reads the manifest stored at <paramref name="key"/>. Throws when it is not valid or was written by a newer
    /// version of the store.
    /// </summary>
    public static ConversationManifest ReadManifest(JsonElement value, string key)
    {
        ConversationManifest? manifest;
        try
        {
            manifest = value.Deserialize(ConversationStoreJsonContext.Default.ConversationManifest);
        }
        catch (JsonException ex)
        {
            throw new ConversationStoreException($"The conversation manifest at '{key}' is not valid.", ex);
        }

        if (manifest is null)
        {
            throw new ConversationStoreException($"The conversation manifest at '{key}' is not valid.");
        }

        if (manifest.Version > ConversationManifest.CurrentVersion)
        {
            throw new ConversationStoreException(
                $"The conversation at '{key}' was written by a newer version of the store (format {manifest.Version}).");
        }

        return manifest;
    }

    /// <summary>A deep copy of a manifest, made by writing it and reading it back.</summary>
    public static ConversationManifest Clone(ConversationManifest manifest) =>
        JsonSerializer.Deserialize(Serialize(manifest), ConversationStoreJsonContext.Default.ConversationManifest)!;

    /// <summary>Reads the chunk stored at <paramref name="key"/>. Throws when it is not valid.</summary>
    public static ConversationChunk ReadChunk(JsonElement document, string key)
    {
        try
        {
            return document.Deserialize(ConversationStoreJsonContext.Default.ConversationChunk)
                ?? throw new ConversationStoreException($"The conversation chunk at '{key}' is not valid.");
        }
        catch (JsonException ex)
        {
            throw new ConversationStoreException($"The conversation chunk at '{key}' is not valid.", ex);
        }
    }

    /// <summary>The size, in bytes, of an entry inside a chunk document.</summary>
    public static int EntryBytes(StoredConversationEntry entry) =>
        JsonSerializer.SerializeToUtf8Bytes(entry, ConversationStoreJsonContext.Default.StoredConversationEntry).Length;

    /// <summary>
    /// Creates the chunk document holding <paramref name="entries"/>, and the descriptor the manifest keeps for it.
    /// </summary>
    public static (ChunkDescriptor Descriptor, JsonElement Document) CreateChunk(
        List<StoredConversationEntry> entries, string chunkId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ConversationChunk { Entries = entries },
            ConversationStoreJsonContext.Default.ConversationChunk);
        using var parsed = JsonDocument.Parse(bytes);
        var descriptor = new ChunkDescriptor
        {
            Id = chunkId,
            FirstSequence = entries[0].Sequence,
            Count = entries.Count,
            Bytes = bytes.Length,
        };
        return (descriptor, parsed.RootElement.Clone());
    }

    /// <summary>
    /// The stored entries of a turn's messages, numbered from <paramref name="firstSequence"/>.
    /// </summary>
    public static List<StoredConversationEntry> ToStoredEntries(
        ConversationTurn turn, long firstSequence, long createdMs, long? summaryOf) =>
        turn.Messages.Select((message, i) => new StoredConversationEntry
        {
            Sequence = firstSequence + i,
            TurnId = turn.TurnId,
            AgentName = turn.AgentName,
            ChatClientKey = turn.ChatClientKey,
            CreatedMs = createdMs,
            SummaryOf = summaryOf,
            Message = StoredChatMessage.From(message),
        }).ToList();

    /// <summary>The public entry a stored entry is loaded as.</summary>
    public static ConversationEntry ToEntry(StoredConversationEntry stored) => new(
        stored.Sequence,
        stored.TurnId,
        stored.AgentName,
        stored.ChatClientKey,
        DateTimeOffset.FromUnixTimeMilliseconds(stored.CreatedMs),
        stored.Message.ToWorkflowChatMessage(),
        stored.SummaryOf);
}
