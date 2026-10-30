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

using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

/// <summary>
/// The two backend operations both the store and <see cref="ConversationTtlRenewer"/> need: reading a manifest
/// with its ETag, and deleting chunks best effort.
/// </summary>
internal static partial class ConversationStateBackendExtensions
{
    /// <summary>
    /// Reads the manifest at <paramref name="key"/> and its ETag; both are null when the key is absent. Throws when
    /// the manifest is not valid, or when the state store returned no ETag for it.
    /// </summary>
    public static async Task<(ConversationManifest? Manifest, string? ETag)> ReadManifestAsync(
        this IConversationStateBackend backend, string key, string stateStoreName, CancellationToken cancellationToken)
    {
        var (value, etag) = await backend.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return (null, null);
        }

        var manifest = ConversationDocumentCodec.ReadManifest(value.Value, key);

        if (string.IsNullOrEmpty(etag))
        {
            throw new ConversationStoreException(
                $"State store '{stateStoreName}' returned no ETag for '{key}'. The conversation store needs a state store that supports ETags.");
        }

        return (manifest, etag);
    }

    /// <summary>Deletes <paramref name="keys"/>, logging instead of throwing when the delete fails.</summary>
    public static async Task TryDeleteAsync(
        this IConversationStateBackend backend, List<string> keys, ILogger logger, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        try
        {
            await backend.DeleteBulkAsync(keys, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unreferenced chunks are harmless; the TTL removes them when one is set.
            LogDeleteFailed(logger, ex, keys.Count);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not delete {Count} unreferenced conversation chunk(s); they are left for the state store TTL.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, int count);
}
