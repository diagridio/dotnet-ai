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

namespace Diagrid.AI.Microsoft.AgentFramework.Conversations;

/// <summary>
/// A conversation store operation failed.
/// </summary>
public class ConversationStoreException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What failed.</param>
    public ConversationStoreException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with the error that caused it.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The error that caused it.</param>
    public ConversationStoreException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The requested conversation history no longer exists, because the conversation was deleted or expired.
/// </summary>
public sealed class ConversationNotFoundException : ConversationStoreException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="conversationId">The conversation that was not found.</param>
    /// <param name="message">What was missing.</param>
    public ConversationNotFoundException(string conversationId, string message)
        : base(message)
    {
        ConversationId = conversationId;
    }

    /// <summary>
    /// The conversation that was not found.
    /// </summary>
    public string ConversationId { get; }
}
