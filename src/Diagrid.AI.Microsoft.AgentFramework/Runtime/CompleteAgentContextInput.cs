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
/// Input payload for <see cref="CompleteAgentContextActivity"/>. Exactly one of
/// <see cref="ResponseMessages"/> or <see cref="ErrorMessage"/> is expected to be set, mirroring the
/// two <c>AIContextProvider.InvokedContext</c> constructor overloads (success vs. exception).
/// </summary>
internal sealed record CompleteAgentContextInput(
    string AgentName,
    string? ChatClientKey,
    List<WorkflowChatMessage> RequestMessages,
    List<WorkflowChatMessage>? ResponseMessages,
    string? ErrorMessage,
    Dictionary<string, string?>? TelemetryBaggage = null);
