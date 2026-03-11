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

using System.Text.Json.Serialization.Metadata;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Contract for resolving source-generated <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
/// </summary>
public interface IAgentJsonTypeInfoResolver
{
    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>, or <c>null</c> if not registered.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <returns>The resolved type info, or <c>null</c>.</returns>
    JsonTypeInfo<T>? GetTypeInfo<T>();
}
