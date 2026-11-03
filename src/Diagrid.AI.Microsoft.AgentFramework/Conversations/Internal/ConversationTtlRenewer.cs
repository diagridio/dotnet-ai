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
/// Keeps a conversation's documents from expiring while it is in use: renews the manifest's TTL on read, and
/// rewrites chunks so they keep outliving the manifest. The rules appends and reads share live here.
/// </summary>
internal sealed partial class ConversationTtlRenewer
{
    private readonly IConversationStateBackend _backend;
    private readonly DaprConversationStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public ConversationTtlRenewer(
        IConversationStateBackend backend,
        DaprConversationStoreOptions options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The <c>ttlInSeconds</c> chunks are written with: twice the manifest's, so they outlive it.</summary>
    public int? ChunkTtlSeconds => _options.TimeToLiveSeconds * 2;

    /// <summary>
    /// Renews a conversation's TTL when it is read between half the TTL and a margin before it expires, so a run that
    /// keeps reading its history at least that often does not lose it. A single pause longer than the TTL (between two
    /// reads, or after the last one) still does: size <see cref="DaprConversationStoreOptions.TimeToLive"/> for the
    /// longest pause. The chunks are rewritten first when half their own margin is used (so they keep outliving the
    /// manifest), then the manifest is written again unchanged, conditioned on the ETag just read.
    /// </summary>
    /// <remarks>
    /// No renewal is tried in the last stretch before expiry (<see cref="RenewalMargin"/>), and the clock is checked
    /// again before the manifest is written: Redis accepts a save conditioned on any ETag once the key is gone, so a
    /// renewal that reached an expired manifest would bring the conversation back. If the manifest write loses a
    /// race or fails after chunks were rewritten, the chunks the current manifest no longer references (deleted by a
    /// concurrent delete or fold while they were being rewritten) are deleted again. Best effort throughout: a
    /// failure is logged and leaves the load as it was.
    /// </remarks>
    public async Task RenewTtlAsync(
        string key, string conversationId, ConversationManifest live, string? etag, CancellationToken cancellationToken)
    {
        if (_options.TimeToLive is not { } ttl || etag is null)
        {
            return;
        }

        var ttlMs = (long)ttl.TotalMilliseconds;
        var lastWritten = live.ModifiedMs;
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (now - lastWritten < ttlMs / 2 || !SafeToRenew(now))
        {
            return;
        }

        var refreshed = false;
        var renewed = false;
        try
        {
            if (live.Chunks.Count > 0 && ChunksDueForRefresh(live.ChunkTtlSeconds, live.ChunksWrittenMs, now))
            {
                if (!await RefreshChunksAsync(key, live.Chunks, ChunkTtlSeconds, cancellationToken).ConfigureAwait(false))
                {
                    // A concurrent append replaced a chunk, so it moved the manifest and renewed the TTL itself.
                    return;
                }

                refreshed = true;
            }

            // The chunk writes took time: write the manifest only if it still cannot have expired.
            now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            if (!SafeToRenew(now))
            {
                return;
            }

            var renewal = ConversationDocumentCodec.Clone(live);
            if (refreshed)
            {
                renewal.ChunksWrittenMs = now;
                renewal.ChunkTtlSeconds = ChunkTtlSeconds;
            }

            renewal.ModifiedMs = Math.Max(now, lastWritten + 1);
            var document = ConversationDocumentCodec.Serialize(renewal);
            renewed = await _backend.TrySaveAsync(key, document, etag, _options.TimeToLiveSeconds, cancellationToken).ConfigureAwait(false);
            if (renewed)
            {
                LogTtlRenewed(conversationId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTtlRenewFailed(ex, conversationId);
        }
        finally
        {
            if (refreshed && !renewed)
            {
                await DeleteResurrectedChunksAsync(key, live.Chunks, conversationId).ConfigureAwait(false);
            }
        }

        bool SafeToRenew(long at) => at - lastWritten < ttlMs - RenewalMargin(ttlMs);
    }

    /// <summary>
    /// Whether chunks written with <paramref name="writtenWithTtl"/> at <paramref name="writtenMs"/> must be written
    /// again so they keep outliving the manifest: the configured TTL changed, or half their margin is used. The one
    /// rule appends and reads share.
    /// </summary>
    public bool ChunksDueForRefresh(int? writtenWithTtl, long writtenMs, long now) =>
        writtenWithTtl != ChunkTtlSeconds
        || (_options.TimeToLive is { } ttl && now - writtenMs >= (long)ttl.TotalMilliseconds / 2);

    /// <summary>
    /// Rewrites the kept chunks unchanged with a fresh TTL, so they outlive the manifest. Returns false when one
    /// of them is gone, which means a concurrent append has moved the manifest.
    /// </summary>
    public async Task<bool> RefreshChunksAsync(
        string key, List<ChunkDescriptor> kept, int? ttlSeconds, CancellationToken cancellationToken)
    {
        var keys = kept.Select(c => ConversationKeys.ChunkKey(key, c.Id)).ToList();
        var documents = await _backend.GetBulkAsync(keys, cancellationToken).ConfigureAwait(false);
        if (keys.Any(k => !documents.ContainsKey(k)))
        {
            return false;
        }

        foreach (var chunkKey in keys)
        {
            await _backend.SaveAsync(chunkKey, documents[chunkKey], ttlSeconds, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The last stretch before a manifest expires in which it is not renewed on read: a quarter of the TTL. It scales
    /// with the TTL, so a renewal window (from half the TTL to three quarters) always exists.
    /// </summary>
    private static long RenewalMargin(long ttlMs) => ttlMs / 4;

    /// <summary>
    /// After a renewal rewrote chunks but did not commit its manifest: deletes those the current manifest does not
    /// reference. A concurrent delete or fold had deleted them, and the rewrite brought them back. Nothing is deleted
    /// when the manifest is absent, because a pending commit could still land on the missing key.
    /// </summary>
    private async Task DeleteResurrectedChunksAsync(string key, List<ChunkDescriptor> rewritten, string conversationId)
    {
        try
        {
            var (current, _) = await _backend.ReadManifestAsync(key, _options.StateStoreName, CancellationToken.None)
                .ConfigureAwait(false);
            if (current is null)
            {
                return;
            }

            var referenced = current.Chunks.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            await _backend.TryDeleteAsync(
                rewritten.Where(c => !referenced.Contains(c.Id)).Select(c => ConversationKeys.ChunkKey(key, c.Id)).ToList(),
                _logger, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTtlRenewFailed(ex, conversationId);
        }
    }

    [LoggerMessage(LogLevel.Debug, "Conversation '{ConversationId}' was read after half its TTL; its expiry was renewed.")]
    private partial void LogTtlRenewed(string conversationId);

    [LoggerMessage(LogLevel.Warning, "Could not renew the TTL of conversation '{ConversationId}' while reading it; the next read or append tries again.")]
    private partial void LogTtlRenewFailed(Exception exception, string conversationId);
}
