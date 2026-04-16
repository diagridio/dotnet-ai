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

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Input payload for <see cref="CallLlmActivity"/>. Contains the agent name and the
/// conversation history needed for a single LLM turn.
/// </summary>
internal sealed record CallLlmInput(
    string AgentName,
    string? ChatClientKey,
    List<WorkflowChatMessage> Messages);
