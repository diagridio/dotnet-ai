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
/// The keys the conversation store writes: a manifest at <c>{KeyPrefix}{conversationId}</c> and its chunks at
/// <c>{manifestKey}#{chunkId}</c>.
/// </summary>
internal static class ConversationKeys
{
    /// <summary>
    /// The characters with a meaning in keys: <c>|</c> (Dapr separates the component's key prefix with <c>||</c>)
    /// and <c>#</c> (separates a chunk id). A key prefix cannot contain them, and a conversation id has them escaped.
    /// </summary>
    public const string ReservedCharacters = "|#";

    // '%' is escaped too, because it starts an escape.
    private const string EscapedCharacters = "%" + ReservedCharacters;

    /// <summary>The key of a conversation's manifest.</summary>
    public static string ManifestKey(string keyPrefix, string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        if (conversationId.Length > DaprConversationStoreOptions.MaxConversationIdLength)
        {
            throw new ArgumentException(
                $"A conversation id can have at most {DaprConversationStoreOptions.MaxConversationIdLength} characters.",
                nameof(conversationId));
        }

        return keyPrefix + EscapeKey(conversationId);
    }

    /// <summary>The key of a chunk the manifest at <paramref name="manifestKey"/> references.</summary>
    public static string ChunkKey(string manifestKey, string chunkId) => $"{manifestKey}#{chunkId}";

    /// <summary>
    /// Percent-escapes the characters with a meaning in keys: <c>|</c> (Dapr separates the component's key
    /// prefix with <c>||</c>), <c>#</c> (separates a chunk id) and <c>%</c> itself, so that two conversation
    /// ids never share a key and an id never reaches another conversation's chunks.
    /// </summary>
    private static string EscapeKey(string conversationId)
    {
        if (conversationId.AsSpan().IndexOfAny(EscapedCharacters) < 0)
        {
            return conversationId;
        }

        return conversationId
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("|", "%7C", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal);
    }
}
