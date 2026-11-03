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

using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// Settings of the conversation store kept in a Dapr state store.
/// </summary>
public sealed class DaprConversationStoreOptions
{
    /// <summary>
    /// The minimum <see cref="TurnDedupeWindow"/>.
    /// </summary>
    public const int MinTurnDedupeWindow = 16;

    /// <summary>
    /// The longest conversation id accepted.
    /// </summary>
    public const int MaxConversationIdLength = 256;

    /// <summary>
    /// The Dapr state store component. It must support ETags, and also TTL when <see cref="TimeToLive"/> is set.
    /// </summary>
    public string StateStoreName { get; set; } = string.Empty;

    /// <summary>
    /// The prefix of every key the store writes. The component adds its own key prefix (the app id by default)
    /// in front of it. It cannot contain <c>|</c> or <c>#</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "agent-conversation:";

    /// <summary>
    /// The largest size, in bytes, of the entries kept in one stored document. Keep it below the state store's
    /// value limit (400 KB on DynamoDB). A single entry larger than this is stored on its own.
    /// </summary>
    public int MaxChunkBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// How many of the most recent turn ids are remembered, so that a repeated append of one of them changes
    /// nothing. At least <see cref="MinTurnDedupeWindow"/>.
    /// </summary>
    public int TurnDedupeWindow { get; set; } = 128;

    /// <summary>
    /// How many times an append tries to commit when other appends to the same conversation keep winning,
    /// before it throws <see cref="ConversationStoreException"/>.
    /// </summary>
    public int MaxCommitAttempts { get; set; } = 10;

    /// <summary>
    /// When set, a conversation expires this long after it was last written: by an append, or by a read between half
    /// of it and a margin before it ends. A run that reads its history at least that often keeps it; a single pause
    /// longer than this between two reads still loses it, so size it for the longest pause. At least one second.
    /// </summary>
    /// <remarks>
    /// Without it, stored conversations are kept until they are deleted, and so are the rare documents left
    /// behind by an append whose outcome could not be confirmed. Do not set <c>ttlInSeconds</c> on the state
    /// store component instead: it would also expire stored messages a conversation still references.
    /// </remarks>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentException">A setting is out of range.</exception>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(StateStoreName))
        {
            throw new ArgumentException("A state store name is required.", nameof(StateStoreName));
        }

        ArgumentNullException.ThrowIfNull(KeyPrefix, nameof(KeyPrefix));
        if (KeyPrefix.AsSpan().IndexOfAny(ConversationKeys.ReservedCharacters) >= 0)
        {
            throw new ArgumentException($"{nameof(KeyPrefix)} cannot contain '|' or '#'.", nameof(KeyPrefix));
        }

        if (MaxChunkBytes <= 0)
        {
            throw new ArgumentException($"{nameof(MaxChunkBytes)} must be positive.", nameof(MaxChunkBytes));
        }

        if (TurnDedupeWindow < MinTurnDedupeWindow)
        {
            throw new ArgumentException(
                $"{nameof(TurnDedupeWindow)} must be at least {MinTurnDedupeWindow}.", nameof(TurnDedupeWindow));
        }

        if (MaxCommitAttempts <= 0)
        {
            throw new ArgumentException($"{nameof(MaxCommitAttempts)} must be positive.", nameof(MaxCommitAttempts));
        }

        if (TimeToLive is { } ttl && ttl < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentException($"{nameof(TimeToLive)} must be at least one second.", nameof(TimeToLive));
        }
    }

    /// <summary>
    /// The TTL in whole seconds, rounded up, or <see langword="null"/> when there is none.
    /// </summary>
    internal int? TimeToLiveSeconds =>
        TimeToLive is { } ttl ? (int)Math.Min(int.MaxValue / 2, Math.Ceiling(ttl.TotalSeconds)) : null;
}
