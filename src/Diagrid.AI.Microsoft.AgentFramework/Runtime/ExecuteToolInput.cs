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
/// Input payload for <see cref="ExecuteToolActivity"/>. Identifies the tool to invoke
/// by agent name and function name (resolved from <see cref="ToolRegistry"/>).
/// </summary>
internal sealed record ExecuteToolInput(
    string AgentName,
    string FunctionName,
    string CallId,
    string ArgumentsJson);
