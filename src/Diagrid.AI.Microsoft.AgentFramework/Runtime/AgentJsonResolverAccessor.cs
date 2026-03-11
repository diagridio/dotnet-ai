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

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Static accessor for JSOn type info resolver used by
/// workflows during deterministic parsing.
/// </summary>
public static class AgentJsonResolverAccessor
{
    private static IAgentJsonTypeInfoResolver? _resolver;

    /// <summary>
    /// Initializes the global resolver once.
    /// </summary>
    public static void Initialize(IAgentJsonTypeInfoResolver resolver)
    {
        _resolver ??= resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// Gets the global resolver or throws if not initialized.
    /// </summary>
    public static IAgentJsonTypeInfoResolver Resolver =>
        _resolver ?? throw new InvalidOperationException(
            $"No {nameof(IAgentJsonTypeInfoResolver)} was initialized. " +
            $"Ensure you registered source-generated contexts in AddDaprAgent(...).");
}
