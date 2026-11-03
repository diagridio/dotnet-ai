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

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

/// <summary>
/// Decides how an append's entries are split into chunks, so chunks stay near
/// <see cref="DaprConversationStoreOptions.MaxChunkBytes"/>. Pure: reading and writing chunks is the store's job.
/// </summary>
internal static class ChunkPlanner
{
    // Bytes a chunk document adds around its entries: {"entries":[...]}.
    public const int ChunkEnvelopeBytes = 14;

    /// <summary>
    /// Whether the last of the <paramref name="chunks"/> a manifest references is small enough to be folded into
    /// the next append's chunks: less than half full.
    /// </summary>
    public static bool ShouldFoldTail(IReadOnlyList<ChunkDescriptor> chunks, int maxChunkBytes) =>
        chunks.Count > 0 && chunks[^1].Bytes < maxChunkBytes / 2;

    /// <summary>
    /// Splits entries into chunks greedily by serialized size. An entry larger than the limit gets a chunk of its own.
    /// </summary>
    public static List<List<StoredConversationEntry>> Pack(
        IReadOnlyList<StoredConversationEntry> entries, int maxChunkBytes)
    {
        var chunks = new List<List<StoredConversationEntry>>();
        var current = new List<StoredConversationEntry>();
        var currentBytes = ChunkEnvelopeBytes;

        foreach (var entry in entries)
        {
            var entryBytes = ConversationDocumentCodec.EntryBytes(entry);
            var separator = current.Count > 0 ? 1 : 0;
            if (current.Count > 0 && currentBytes + separator + entryBytes > maxChunkBytes)
            {
                chunks.Add(current);
                current = [];
                currentBytes = ChunkEnvelopeBytes;
                separator = 0;
            }

            current.Add(entry);
            currentBytes += separator + entryBytes;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }
}
