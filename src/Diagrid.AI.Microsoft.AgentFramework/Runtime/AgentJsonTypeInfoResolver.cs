// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Aggregates one or more source-generated JsonSerializerContext instances.
/// </summary>
internal sealed class AgentJsonTypeInfoResolver : IAgentJsonTypeInfoResolver
{
    private readonly JsonSerializerOptions _options;

    public AgentJsonTypeInfoResolver(IEnumerable<JsonSerializerContext> contexts)
    {
        // Build options with a resolver chain from the provided contexts
        _options = new();
        foreach (var context in contexts)
        {
            _options.TypeInfoResolverChain.Add(context);
        }
    }

    // <inheritdoc />
    public JsonTypeInfo<T>? GetTypeInfo<T>() => (JsonTypeInfo<T>?)_options.GetTypeInfo(typeof(T));
}
