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
/// An LLM call does not fit the agent's context window (<c>WithContextWindow</c>) even with every earlier turn left
/// out: its instructions, the context providers' messages and the run's own messages are already too large.
/// </summary>
/// <remarks>
/// Internal: it fails the LLM activity, so callers see the run fail with its message, not this type.
/// </remarks>
internal sealed class ContextWindowExceededException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What did not fit, and how to fix it.</param>
    public ContextWindowExceededException(string message)
        : base(message)
    {
    }
}
