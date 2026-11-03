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
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// <see cref="IConversationStore"/> kept in a Dapr state store. Port of dapr-agents <c>DaprSessionStore</c>
/// (<c>dapr_agents/agents/executors/dapr_session_store.py</c>).
/// </summary>
/// <remarks>
/// <para>
/// A conversation is a manifest at <c>{KeyPrefix}{conversationId}</c> plus immutable chunks of entries at
/// <c>{manifestKey}#{chunkId}</c>. An append writes new chunks first and then commits a new manifest,
/// conditioned on the ETag it read, so a turn becomes visible all at once or not at all. A small last
/// chunk is folded into the next append's chunk, so chunks stay near <see cref="DaprConversationStoreOptions.MaxChunkBytes"/>.
/// </para>
/// <para>
/// Unlike the Python store, an append whose commit outcome is unknown (a lost reply, a sidecar retry) never
/// deletes the chunks it wrote until that commit provably cannot land: the manifest exists and no longer
/// has the ETag the commit was conditioned on. An absent manifest proves nothing, because Redis accepts a
/// save conditioned on any ETag when the key does not exist. Deleting chunks earlier could leave a committed
/// manifest pointing at missing chunks, which would make the conversation impossible to load. Chunks that
/// cannot be proven safe to delete are left to the TTL.
/// </para>
/// <para>
/// For the same reason <see cref="DeleteAsync"/> never removes the manifest key: it replaces the manifest
/// with a tombstone, conditioned on the ETag it read, and only then deletes the chunks. A commit conditioned
/// on an older ETag can no longer land on it. Each conversation also gets a random incarnation id when it is
/// created, so a load pinned to an earlier incarnation fails instead of returning a newer conversation's
/// history under the same id.
/// </para>
/// </remarks>
internal sealed partial class DaprConversationStore : IConversationStore
{
    private const int MaxLoadAttempts = 3;

    private readonly IConversationStateBackend _backend;
    private readonly DaprConversationStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ConversationTtlRenewer _ttl;

    public DaprConversationStore(
        IConversationStateBackend backend,
        DaprConversationStoreOptions options,
        TimeProvider timeProvider,
        ILogger<DaprConversationStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _backend = backend;
        _options = options;
        _timeProvider = timeProvider;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _ttl = new ConversationTtlRenewer(backend, options, timeProvider, _logger);
        Delay = (delay, ct) => Task.Delay(delay, _timeProvider, ct);
    }

    /// <summary>
    /// Waits between commit attempts. Replaced by tests, so retries do not wait.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; }

    /// <summary>The <c>ttlInSeconds</c> value that removes an expiry a key was written with.</summary>
    private const int PersistTtl = -1;

    /// <inheritdoc />
    public async Task<ConversationSnapshot> LoadAsync(
        string conversationId,
        long? upToSequence = null,
        string? incarnation = null,
        CancellationToken cancellationToken = default)
    {
        var key = ManifestKey(conversationId);
        if (upToSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upToSequence), upToSequence, "The sequence cannot be negative.");
        }

        for (var attempt = 1; attempt <= MaxLoadAttempts; attempt++)
        {
            var (manifest, etag) = await ReadManifestAsync(key, cancellationToken).ConfigureAwait(false);
            if (manifest is not { Deleted: false } live)
            {
                if (upToSequence is null or 0)
                {
                    return new ConversationSnapshot(0, []);
                }

                throw new ConversationNotFoundException(conversationId,
                    $"Conversation '{conversationId}' does not exist, so its first {upToSequence} entries cannot be loaded. It was deleted or expired.");
            }

            if (incarnation is not null && upToSequence is not 0
                && !string.Equals(incarnation, live.Incarnation, StringComparison.Ordinal))
            {
                throw new ConversationNotFoundException(conversationId,
                    $"Conversation '{conversationId}' was deleted or expired and created again since its history was first read.");
            }

            var upTo = upToSequence ?? live.NextSequence;
            if (upTo > live.NextSequence)
            {
                throw new ConversationNotFoundException(conversationId,
                    $"Conversation '{conversationId}' has {live.NextSequence} entries, fewer than the {upTo} requested. It was deleted and created again.");
            }

            if (attempt == 1)
            {
                await _ttl.RenewTtlAsync(key, conversationId, live, etag, cancellationToken).ConfigureAwait(false);
            }

            if (upTo == 0)
            {
                return new ConversationSnapshot(0, [], live.Incarnation);
            }

            if (!ManifestRetention.TryFindSummary(live, upTo, out var summary))
            {
                throw new ConversationNotFoundException(conversationId,
                    $"Conversation '{conversationId}' was compacted too many times since this history was first read; the summary it used is gone.");
            }

            var from = summary?.CoversUpToSequence ?? 0;

            var range = await ReadRangeAsync(key, conversationId, live, from, upTo, attempt, cancellationToken).ConfigureAwait(false);
            if (range is null)
            {
                continue;
            }

            var entries = new List<ConversationEntry>(range.Count);
            if (summary is not null)
            {
                var summaryEntry = range.Find(e => e.Sequence == summary.Sequence);
                if (summaryEntry?.SummaryOf is null)
                {
                    throw new ConversationStoreException(
                        $"Conversation '{conversationId}' is corrupt: entry {summary.Sequence} is not the summary its manifest lists.");
                }

                entries.Add(ConversationDocumentCodec.ToEntry(summaryEntry));
            }

            // Older summaries are superseded by the one above (or by the full history when there is none).
            entries.AddRange(range.Where(e => e.SummaryOf is null).Select(ConversationDocumentCodec.ToEntry));

            return new ConversationSnapshot(upTo, entries, live.Incarnation);
        }

        throw new ConversationStoreException(
            $"Could not load conversation '{conversationId}': a chunk it references was still missing after {MaxLoadAttempts} attempts.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntry>> LoadEntriesAsync(
        string conversationId, long fromSequence, long? toSequence = null, CancellationToken cancellationToken = default)
    {
        var key = ManifestKey(conversationId);
        if (fromSequence < 0 || toSequence < fromSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(fromSequence), fromSequence, "The range is not valid.");
        }

        for (var attempt = 1; attempt <= MaxLoadAttempts; attempt++)
        {
            var (manifest, _) = await ReadManifestAsync(key, cancellationToken).ConfigureAwait(false);
            if (manifest is not { Deleted: false } live)
            {
                return [];
            }

            var to = Math.Min(toSequence ?? live.NextSequence, live.NextSequence);
            if (to <= fromSequence)
            {
                return [];
            }

            var range = await ReadRangeAsync(key, conversationId, live, fromSequence, to, attempt, cancellationToken).ConfigureAwait(false);
            if (range is null)
            {
                continue;
            }

            return range.Select(ConversationDocumentCodec.ToEntry).ToList();
        }

        throw new ConversationStoreException(
            $"Could not load conversation '{conversationId}': a chunk it references was still missing after {MaxLoadAttempts} attempts.");
    }

    /// <summary>
    /// Reads the committed entries in [<paramref name="from"/>, <paramref name="to"/>) from the chunks
    /// <paramref name="manifest"/> references, ordered by sequence. Returns <see langword="null"/> when a chunk is
    /// missing (a concurrent append folded and deleted it after the manifest was read), so the caller reads the
    /// manifest again. Throws when the range has a gap, a duplicate or a missing entry, so no caller gets a
    /// silently truncated history.
    /// </summary>
    private async Task<List<StoredConversationEntry>?> ReadRangeAsync(
        string key,
        string conversationId,
        ConversationManifest manifest,
        long from,
        long to,
        int attempt,
        CancellationToken cancellationToken)
    {
        var keys = manifest.Chunks
            .Where(c => c.FirstSequence < to && c.FirstSequence + c.Count > from)
            .Select(c => ConversationKeys.ChunkKey(key, c.Id))
            .ToList();
        var documents = await _backend.GetBulkAsync(keys, cancellationToken).ConfigureAwait(false);
        if (keys.Any(k => !documents.ContainsKey(k)))
        {
            LogChunkMissingOnLoad(conversationId, attempt);
            return null;
        }

        var range = new List<StoredConversationEntry>((int)Math.Min(to - from, int.MaxValue));
        foreach (var chunkKey in keys)
        {
            foreach (var stored in ConversationDocumentCodec.ReadChunk(documents[chunkKey], chunkKey).Entries)
            {
                if (stored.Sequence >= from && stored.Sequence < to)
                {
                    range.Add(stored);
                }
            }
        }

        range.Sort((x, y) => x.Sequence.CompareTo(y.Sequence));
        for (var i = 0; i < range.Count; i++)
        {
            if (range[i].Sequence != from + i)
            {
                throw new ConversationStoreException(
                    $"Conversation '{conversationId}' is corrupt: expected entry {from + i}, found {range[i].Sequence}.");
            }
        }

        if (range.Count != to - from)
        {
            throw new ConversationStoreException(
                $"Conversation '{conversationId}' is corrupt: expected {to - from} entries, found {range.Count}.");
        }

        return range;
    }

    /// <inheritdoc />
    public Task<ConversationAppendResult> AppendTurnAsync(
        ConversationTurn turn, CancellationToken cancellationToken = default) =>
        AppendCoreAsync(turn, summaryOf: null, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationAppendResult> AppendSummaryAsync(
        ConversationSummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(summary.Message, nameof(summary));
        if (summary.CoversUpToSequence <= 0)
        {
            throw new ArgumentException("A summary must cover at least one entry.", nameof(summary));
        }

        // A summary is a one-message turn; its sequence comes after everything it covers.
        var turn = new ConversationTurn(summary.ConversationId, summary.SummaryId, summary.CoversUpToSequence,
            summary.AgentName, summary.ChatClientKey, [summary.Message], summary.BaseIncarnation);
        return AppendCoreAsync(turn, summary.CoversUpToSequence, cancellationToken);
    }

    private async Task<ConversationAppendResult> AppendCoreAsync(
        ConversationTurn turn, long? summaryOf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var key = ManifestKey(turn.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.TurnId, nameof(turn));
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.AgentName, nameof(turn));
        if (turn.Messages is not { Count: > 0 })
        {
            throw new ArgumentException("A turn needs at least one message.", nameof(turn));
        }

        if (turn.Messages.Any(m => m is null))
        {
            throw new ArgumentException("A turn's messages cannot be null.", nameof(turn));
        }

        if (turn.BaseSequence < 0)
        {
            throw new ArgumentException("A turn's base sequence cannot be negative.", nameof(turn));
        }

        var pending = new List<PendingCommit>();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= _options.MaxCommitAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Delay(Backoff(attempt - 1), cancellationToken).ConfigureAwait(false);
            }

            var (read, etag) = await ReadManifestAsync(key, cancellationToken).ConfigureAwait(false);
            await SettleAsync(key, pending, read, p => CanStillLand(p, read, etag), cancellationToken).ConfigureAwait(false);

            // A tombstone is an empty conversation, replaced by a commit conditioned on its ETag.
            var manifest = IsLive(read) ? read : null;

            // Checked before anything else: a commit whose reply was lost has already landed.
            var stored = manifest?.Turns.FindLast(t => t.Id == turn.TurnId);
            if (stored is not null)
            {
                return new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended,
                    stored.FirstSequence, manifest!.NextSequence, stored.BaseMoved);
            }

            // A turn produced against stored history needs that history to still exist: otherwise the turn
            // would start a new conversation that depends on messages nobody can load any more.
            if (turn.BaseIncarnation is not null
                && !string.Equals(turn.BaseIncarnation, manifest?.Incarnation, StringComparison.Ordinal))
            {
                throw new ConversationNotFoundException(turn.ConversationId,
                    $"Conversation '{turn.ConversationId}' was deleted or expired while turn '{turn.TurnId}' was produced from its history, so the turn is not stored.");
            }

            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var next = manifest?.NextSequence ?? 0;
            if (summaryOf > next)
            {
                throw new ArgumentException(
                    $"The summary covers {summaryOf} entries, but conversation '{turn.ConversationId}' has {next}.", nameof(turn));
            }

            var incarnation = manifest?.Incarnation ?? Guid.NewGuid().ToString("N");
            var baseMoved = next != turn.BaseSequence;
            var entries = ConversationDocumentCodec.ToStoredEntries(turn, next, now, summaryOf);

            // Plan: keep the full chunks, and fold a small last chunk into this append's chunks.
            var kept = manifest?.Chunks.ToList() ?? [];
            string? superseded = null;
            var toWrite = entries;
            if (ChunkPlanner.ShouldFoldTail(kept, _options.MaxChunkBytes))
            {
                var tail = kept[^1];
                var tailKey = ConversationKeys.ChunkKey(key, tail.Id);
                var tailDocuments = await _backend.GetBulkAsync([tailKey], cancellationToken).ConfigureAwait(false);
                if (!tailDocuments.TryGetValue(tailKey, out var tailDocument))
                {
                    // Another append folded and deleted it after this manifest was read, so the manifest moved.
                    LogCommitConflict(turn.ConversationId, attempt);
                    continue;
                }

                superseded = tail.Id;
                kept.RemoveAt(kept.Count - 1);
                toWrite = [.. ConversationDocumentCodec.ReadChunk(tailDocument, tailKey).Entries, .. entries];
            }

            // TTLs: the manifest expires TimeToLive after the last append and chunks get twice that, so they
            // outlive it. Kept chunks are written again once half their margin is used (leaving room for clock
            // skew between writers), or when the configured TTL differs from the one they were written with.
            // Turning the TTL off removes the expiry once, with PersistTtl; otherwise no TTL is sent at all, so
            // stores without TTL support keep working.
            var chunkTtl = _ttl.ChunkTtlSeconds;
            var removeExpiry = chunkTtl is null && manifest?.ChunkTtlSeconds is not null;
            var chunkWriteTtl = chunkTtl ?? (removeExpiry ? PersistTtl : null);
            var manifestWriteTtl = _options.TimeToLiveSeconds ?? (removeExpiry ? PersistTtl : null);
            var chunksWrittenMs = kept.Count > 0 ? manifest!.ChunksWrittenMs : now;
            var refresh = kept.Count > 0 && _ttl.ChunksDueForRefresh(manifest!.ChunkTtlSeconds, chunksWrittenMs, now);
            if (refresh)
            {
                if (!await _ttl.RefreshChunksAsync(key, kept, chunkWriteTtl, cancellationToken).ConfigureAwait(false))
                {
                    LogCommitConflict(turn.ConversationId, attempt);
                    continue;
                }

                chunksWrittenMs = now;
            }

            var chunks = ChunkPlanner.Pack(toWrite, _options.MaxChunkBytes)
                .Select(entriesOfChunk => ConversationDocumentCodec.CreateChunk(entriesOfChunk, Guid.NewGuid().ToString("N")))
                .ToList();
            var commit = new PendingCommit(chunks.Select(c => c.Descriptor.Id).ToList(), etag, superseded);
            await WriteChunksAsync(key, chunks, chunkWriteTtl, cancellationToken).ConfigureAwait(false);
            pending.Add(commit);

            var updated = new ConversationManifest
            {
                NextSequence = next + entries.Count,
                Chunks = [.. kept, .. chunks.Select(c => c.Descriptor)],
                Turns = ManifestRetention.TrimTurns(manifest?.Turns, new TurnRecord
                {
                    Id = turn.TurnId,
                    FirstSequence = next,
                    Count = entries.Count,
                    BaseSequence = turn.BaseSequence,
                    BaseMoved = baseMoved,
                }, _options.TurnDedupeWindow),
                ModifiedMs = Math.Max(now, (read?.ModifiedMs ?? 0) + 1),
                ChunksWrittenMs = chunksWrittenMs,
                ChunkTtlSeconds = chunkTtl,
                Incarnation = incarnation,
                Summaries = manifest?.Summaries,
                SummariesDroppedMax = manifest?.SummariesDroppedMax,
            };
            if (summaryOf is { } covers)
            {
                ManifestRetention.AddSummary(updated, new SummaryRecord { Sequence = next, CoversUpToSequence = covers });
            }

            var document = ConversationDocumentCodec.Serialize(updated);
            bool? committed;
            try
            {
                committed = etag is null
                    ? await _backend.TryCreateAsync(key, document, manifestWriteTtl, cancellationToken).ConfigureAwait(false)
                    : await _backend.TrySaveAsync(key, document, etag, manifestWriteTtl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The write may or may not have landed. Nothing is deleted here; the next read settles it.
                lastError = ex;
                committed = null;
                LogCommitOutcomeUnknown(ex, turn.ConversationId, attempt);
            }

            if (committed == true)
            {
                pending.Remove(commit);

                // This commit moved the ETag, so no earlier attempt of this call can land any more.
                await SettleAsync(key, pending, updated, _ => false, cancellationToken).ConfigureAwait(false);
                if (superseded is not null)
                {
                    await TryDeleteAsync([ConversationKeys.ChunkKey(key, superseded)], cancellationToken).ConfigureAwait(false);
                }

                return new ConversationAppendResult(ConversationAppendStatus.Appended,
                    next, updated.NextSequence, baseMoved);
            }

            if (committed == false)
            {
                LogCommitConflict(turn.ConversationId, attempt);
            }
        }

        throw new ConversationStoreException(
            $"Could not commit turn '{turn.TurnId}' to conversation '{turn.ConversationId}' after {_options.MaxCommitAttempts} attempts.",
            lastError);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var key = ManifestKey(conversationId);
        Exception? lastError = null;
        ConversationManifest? tombstoned = null;

        for (var attempt = 1; attempt <= _options.MaxCommitAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Delay(Backoff(attempt - 1), cancellationToken).ConfigureAwait(false);
            }

            var (manifest, etag) = await ReadManifestAsync(key, cancellationToken).ConfigureAwait(false);
            if (!IsLive(manifest))
            {
                // Either there was nothing to delete, or an earlier attempt's tombstone landed without a reply.
                if (tombstoned is null || manifest is null || manifest.Incarnation != tombstoned.Incarnation)
                {
                    return false;
                }

                await TryDeleteAsync(tombstoned.Chunks.Select(c => ConversationKeys.ChunkKey(key, c.Id)).ToList(), cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            // The key is never removed: a commit conditioned on an older ETag could land on an absent key.
            var tombstone = new ConversationManifest
            {
                Deleted = true,
                Incarnation = manifest!.Incarnation,
                ModifiedMs = Math.Max(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), manifest.ModifiedMs + 1),
            };
            var document = ConversationDocumentCodec.Serialize(tombstone);
            tombstoned = manifest;
            bool committed;
            try
            {
                committed = await _backend.TrySaveAsync(key, document, etag!, _options.TimeToLiveSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                continue;
            }

            if (committed)
            {
                // No commit can reference these chunks any more: every one is conditioned on an older ETag.
                await TryDeleteAsync(manifest.Chunks.Select(c => ConversationKeys.ChunkKey(key, c.Id)).ToList(), cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
        }

        throw new ConversationStoreException(
            $"Could not delete conversation '{conversationId}' after {_options.MaxCommitAttempts} attempts.", lastError);
    }

    internal string ManifestKey(string conversationId) =>
        ConversationKeys.ManifestKey(_options.KeyPrefix, conversationId);

    private static bool IsLive(ConversationManifest? manifest) => manifest is { Deleted: false };

    private Task<(ConversationManifest? Manifest, string? ETag)> ReadManifestAsync(
        string key, CancellationToken cancellationToken) =>
        _backend.ReadManifestAsync(key, _options.StateStoreName, cancellationToken);

    private async Task WriteChunksAsync(
        string key,
        List<(ChunkDescriptor Descriptor, JsonElement Document)> chunks,
        int? ttlSeconds,
        CancellationToken cancellationToken)
    {
        var written = new List<string>(chunks.Count);
        try
        {
            foreach (var (descriptor, document) in chunks)
            {
                var chunkKey = ConversationKeys.ChunkKey(key, descriptor.Id);
                written.Add(chunkKey);
                await _backend.SaveAsync(chunkKey, document, ttlSeconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // No manifest commit referencing these chunks was attempted, so they can be deleted right away.
            await TryDeleteAsync(written, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Whether an earlier commit attempt of this call could still land. A create can land while the key is
    /// absent. A save conditioned on an ETag can land while the store still holds that ETag, and also while the
    /// key is absent, because Redis accepts a conditioned save on a missing key. A tombstone is a manifest
    /// like any other here: it exists, with its own ETag.
    /// </summary>
    private static bool CanStillLand(PendingCommit commit, ConversationManifest? manifest, string? etag) =>
        manifest is null
        || (commit.ConditionETag is not null && string.Equals(commit.ConditionETag, etag, StringComparison.Ordinal));

    /// <summary>
    /// The deletion rule: an attempt's chunks are deleted only once its commit can no longer land and the
    /// current manifest does not reference them. Settled attempts are removed from <paramref name="pending"/>.
    /// </summary>
    private async Task SettleAsync(
        string manifestKey,
        List<PendingCommit> pending,
        ConversationManifest? manifest,
        Func<PendingCommit, bool> canStillLand,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var referenced = manifest?.Chunks.Select(c => c.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var toDelete = new List<string>();
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var commit = pending[i];
            if (canStillLand(commit))
            {
                continue;
            }

            pending.RemoveAt(i);
            toDelete.AddRange(commit.ChunkIds.Where(id => !referenced.Contains(id)));

            // A folded tail is never added back by any commit, so it can go once no manifest references it.
            if (commit.SupersededId is { } superseded && !referenced.Contains(superseded))
            {
                toDelete.Add(superseded);
            }
        }

        await TryDeleteAsync(toDelete.Select(id => ConversationKeys.ChunkKey(manifestKey, id)).ToList(), cancellationToken)
            .ConfigureAwait(false);
    }

    private Task TryDeleteAsync(List<string> keys, CancellationToken cancellationToken) =>
        _backend.TryDeleteAsync(keys, _logger, cancellationToken);

    private static TimeSpan Backoff(int failedAttempts)
    {
        var baseMs = Math.Min(50 * failedAttempts, 500);
        return TimeSpan.FromMilliseconds(baseMs * (1 + (Random.Shared.NextDouble() * 0.25)));
    }

    /// <summary>
    /// A commit this call attempted: the chunks it wrote, the ETag it was conditioned on (null for a first
    /// write), and the last chunk it folded.
    /// </summary>
    private sealed record PendingCommit(List<string> ChunkIds, string? ConditionETag, string? SupersededId);

    [LoggerMessage(LogLevel.Debug, "Append to conversation '{ConversationId}' lost a race on attempt {Attempt}; retrying.")]
    private partial void LogCommitConflict(string conversationId, int attempt);

    [LoggerMessage(LogLevel.Warning, "Append to conversation '{ConversationId}' failed on attempt {Attempt} and may or may not have been stored; checking before retrying.")]
    private partial void LogCommitOutcomeUnknown(Exception exception, string conversationId, int attempt);

    [LoggerMessage(LogLevel.Debug, "A chunk of conversation '{ConversationId}' was replaced while loading it (attempt {Attempt}); reading it again.")]
    private partial void LogChunkMissingOnLoad(string conversationId, int attempt);
}
