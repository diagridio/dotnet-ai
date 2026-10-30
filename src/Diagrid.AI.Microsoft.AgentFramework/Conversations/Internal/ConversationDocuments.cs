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
using System.Text.Json.Serialization;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

// The documents the store writes. They are kept apart from the public WorkflowChatMessage types, so
// renaming a public member never changes what is already stored. Every name is set explicitly.

/// <summary>
/// The document at a conversation's key: what has been committed, and where it is stored.
/// It is the only document ever changed in place, always conditioned on its ETag.
/// </summary>
internal sealed class ConversationManifest
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// How many summary records <see cref="Summaries"/> keeps. It bounds a persisted document; a load pinned to a
    /// prefix whose summary was dropped fails (<see cref="SummariesDroppedMax"/>).
    /// </summary>
    public const int MaxSummaries = 32;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// A random id given to the conversation when it is created, so a reader pinned to an earlier
    /// conversation under the same id can tell it was deleted or expired and created again.
    /// </summary>
    [JsonPropertyName("incarnation")]
    public string? Incarnation { get; set; }

    /// <summary>
    /// True for the tombstone a delete leaves: an empty conversation. The key itself is never removed,
    /// because Redis accepts a save conditioned on any ETag when the key does not exist.
    /// </summary>
    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Deleted { get; set; }

    [JsonPropertyName("next_seq")]
    public long NextSequence { get; set; }

    [JsonPropertyName("chunks")]
    public List<ChunkDescriptor> Chunks { get; set; } = [];

    /// <summary>The most recent turns, oldest first, for idempotent appends.</summary>
    [JsonPropertyName("turns")]
    public List<TurnRecord> Turns { get; set; } = [];

    /// <summary>
    /// The most recent summaries, oldest first: where each summary entry is and how much of the conversation it
    /// replaces. A load uses the latest one below its prefix.
    /// </summary>
    [JsonPropertyName("summaries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SummaryRecord>? Summaries { get; set; }

    /// <summary>
    /// The sequence of the newest summary record dropped from <see cref="Summaries"/>, or null if none was. A load
    /// pinned to a prefix that used a dropped summary fails instead of quietly loading the full history.
    /// </summary>
    [JsonPropertyName("summaries_dropped_max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SummariesDroppedMax { get; set; }

    /// <summary>Unix milliseconds of the last write: a commit, or a read that renewed the TTL. Always increases.</summary>
    [JsonPropertyName("mtime")]
    public long ModifiedMs { get; set; }

    /// <summary>Unix milliseconds of the oldest TTL write among the referenced chunks.</summary>
    [JsonPropertyName("chunks_written_ms")]
    public long ChunksWrittenMs { get; set; }

    /// <summary>The <c>ttlInSeconds</c> the referenced chunks were written with, or null for none.</summary>
    [JsonPropertyName("chunk_ttl_s")]
    public int? ChunkTtlSeconds { get; set; }
}

/// <summary>A chunk the manifest references: its id and the entries it holds.</summary>
internal sealed class ChunkDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("first_seq")]
    public long FirstSequence { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("bytes")]
    public int Bytes { get; set; }
}

/// <summary>A summary entry: its sequence, and the entries below <see cref="CoversUpToSequence"/> it replaces.</summary>
internal sealed class SummaryRecord
{
    [JsonPropertyName("seq")]
    public long Sequence { get; set; }

    [JsonPropertyName("covers")]
    public long CoversUpToSequence { get; set; }
}

/// <summary>A committed turn, remembered so that appending it again changes nothing.</summary>
internal sealed class TurnRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("first_seq")]
    public long FirstSequence { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("base_seq")]
    public long BaseSequence { get; set; }

    /// <summary>Whether the turn was produced against an older base, as first reported.</summary>
    [JsonPropertyName("base_moved")]
    public bool BaseMoved { get; set; }
}

/// <summary>
/// A stored run of consecutive entries. It is never changed once written, except that a TTL refresh
/// writes the same content again.
/// </summary>
internal sealed class ConversationChunk
{
    [JsonPropertyName("entries")]
    public List<StoredConversationEntry> Entries { get; set; } = [];
}

internal sealed class StoredConversationEntry
{
    [JsonPropertyName("seq")]
    public long Sequence { get; set; }

    [JsonPropertyName("turn_id")]
    public string TurnId { get; set; } = string.Empty;

    [JsonPropertyName("agent")]
    public string AgentName { get; set; } = string.Empty;

    [JsonPropertyName("client_key")]
    public string? ChatClientKey { get; set; }

    [JsonPropertyName("created_ms")]
    public long CreatedMs { get; set; }

    /// <summary>For a summary entry, the sequence below which it replaces the conversation; otherwise null.</summary>
    [JsonPropertyName("summary_of")]
    public long? SummaryOf { get; set; }

    [JsonPropertyName("message")]
    public StoredChatMessage Message { get; set; } = new();
}

internal sealed class StoredChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning")]
    public List<StoredReasoning>? Reasoning { get; set; }

    [JsonPropertyName("function_calls")]
    public List<StoredFunctionCall>? FunctionCalls { get; set; }

    [JsonPropertyName("function_results")]
    public List<StoredFunctionResult>? FunctionResults { get; set; }

    public static StoredChatMessage From(WorkflowChatMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        Reasoning = message.Reasoning?.Select(r => new StoredReasoning
        {
            Text = r.Text,
            ProtectedData = r.ProtectedData,
            Source = r.Source,
        }).ToList(),
        FunctionCalls = message.FunctionCalls?.Select(c => new StoredFunctionCall
        {
            CallId = c.CallId,
            Name = c.Name,
            ArgumentsJson = c.ArgumentsJson,
        }).ToList(),
        FunctionResults = message.FunctionResults?.Select(r => new StoredFunctionResult
        {
            CallId = r.CallId,
            Name = r.Name,
            ResultJson = r.ResultJson,
        }).ToList(),
    };

    public WorkflowChatMessage ToWorkflowChatMessage() => new()
    {
        Role = Role,
        Content = Content,
        Reasoning = Reasoning?.Select(r => new WorkflowReasoningContent
        {
            Text = r.Text,
            ProtectedData = r.ProtectedData,
            Source = r.Source,
        }).ToList(),
        FunctionCalls = FunctionCalls?.Select(c => new WorkflowFunctionCall
        {
            CallId = c.CallId,
            Name = c.Name,
            ArgumentsJson = c.ArgumentsJson,
        }).ToList(),
        FunctionResults = FunctionResults?.Select(r => new WorkflowFunctionResult
        {
            CallId = r.CallId,
            Name = r.Name,
            ResultJson = r.ResultJson,
        }).ToList(),
    };
}

internal sealed class StoredReasoning
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("protected_data")]
    public string? ProtectedData { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

internal sealed class StoredFunctionCall
{
    [JsonPropertyName("call_id")]
    public string CallId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments_json")]
    public string ArgumentsJson { get; set; } = "{}";
}

internal sealed class StoredFunctionResult
{
    [JsonPropertyName("call_id")]
    public string CallId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("result_json")]
    public string? ResultJson { get; set; }
}

/// <summary>
/// Source-generated serialization of the stored documents. Each document is serialized once, to the
/// <see cref="JsonElement"/> handed to the Dapr client, whose own serializer writes a
/// <see cref="JsonElement"/> unchanged.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConversationManifest))]
[JsonSerializable(typeof(ConversationChunk))]
[JsonSerializable(typeof(StoredConversationEntry))]
internal sealed partial class ConversationStoreJsonContext : JsonSerializerContext;
