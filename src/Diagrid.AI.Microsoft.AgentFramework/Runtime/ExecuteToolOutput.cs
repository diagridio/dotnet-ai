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
/// Output payload from <see cref="ExecuteToolActivity"/>.
/// </summary>
internal sealed record ExecuteToolOutput
{
    public string CallId { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string? ResultJson { get; init; }
    public string? Error { get; init; }
}
