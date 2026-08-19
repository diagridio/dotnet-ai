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

using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Helper record to allow DI to collect <see cref="AIContextProvider"/> instances (e.g. a skills
/// provider) that were registered for a named agent via <c>WithContextProviders</c>/<c>WithSkills</c>
/// ahead of the agent's own (lazy) factory materialization.
/// </summary>
/// <param name="AgentName">The name of the agent the providers should be attached to.</param>
/// <param name="ContextProviders">The context providers to attach.</param>
public sealed record ContextProviderRegistration(string AgentName, IReadOnlyList<AIContextProvider> ContextProviders);
