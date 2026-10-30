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

using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Aggregates one or more source-generated JsonSerializerContext instances.
/// </summary>
internal sealed class AgentJsonTypeInfoResolver : IAgentJsonTypeInfoResolver
{
    private readonly JsonSerializerContext[] _contexts;

    public AgentJsonTypeInfoResolver(IEnumerable<JsonSerializerContext> contexts)
    {
        _contexts = contexts.ToArray();
    }

    // <inheritdoc />
    public JsonTypeInfo<T>? GetTypeInfo<T>()
    {
        // Ask each context against its own Options, first registration wins. Chaining the
        // contexts into one shared JsonSerializerOptions instead would bind their metadata to
        // those options and silently drop each context's [JsonSourceGenerationOptions]
        // (e.g. a camelCase naming policy, so "answer" no longer maps to Answer). It would
        // also throw NotSupportedException for an unregistered type rather than return null.
        foreach (var context in _contexts)
        {
            if (context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> typeInfo)
            {
                return typeInfo;
            }
        }

        return null;
    }
}
