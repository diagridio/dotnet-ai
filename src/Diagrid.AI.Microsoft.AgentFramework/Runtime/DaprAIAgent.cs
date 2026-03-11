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
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Default agent used inside Dapr workflows to reference a registered <see cref="AIAgent"/>.
/// </summary>
/// <param name="name">The name of the agent.</param>
internal sealed class DaprAIAgent(string name) : IDaprAIAgent
{
    internal DaprAIAgent(string name, string? chatClientKey)
        : this(name)
    {
        ChatClientKey = chatClientKey;
    }

    /// <inheritdoc />
    public string Name { get; } = name;

    internal string? ChatClientKey { get; }
}
