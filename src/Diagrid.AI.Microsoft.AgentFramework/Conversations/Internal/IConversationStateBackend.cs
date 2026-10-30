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
/// The state operations the conversation store needs. A seam over the Dapr client, so tests can inject
/// ETag conflicts, lost replies and late writes.
/// </summary>
internal interface IConversationStateBackend
{
    /// <summary>Reads a value and its ETag with strong consistency; both are null when the key is absent.</summary>
    Task<(JsonElement? Value, string? ETag)> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Reads several values. A key that is absent is left out of the result.</summary>
    Task<IReadOnlyDictionary<string, JsonElement>> GetBulkAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);

    /// <summary>Writes a value unconditionally.</summary>
    Task SaveAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken);

    /// <summary>Writes a value only if the stored ETag is <paramref name="etag"/>; false when it is not.</summary>
    Task<bool> TrySaveAsync(string key, JsonElement value, string etag, int? ttlSeconds, CancellationToken cancellationToken);

    /// <summary>Writes a value only if the key is absent; false when it exists.</summary>
    Task<bool> TryCreateAsync(string key, JsonElement value, int? ttlSeconds, CancellationToken cancellationToken);

    /// <summary>Deletes several values.</summary>
    Task DeleteBulkAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);
}
