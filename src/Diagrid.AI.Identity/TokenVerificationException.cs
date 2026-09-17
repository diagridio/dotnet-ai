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

namespace Diagrid.AI.Identity;

/// <summary>
/// Signature or claim validation failed.
/// </summary>
public sealed class TokenVerificationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenVerificationException"/> class.
    /// </summary>
    /// <param name="code">One of the <see cref="OAuthErrorCodes"/> values.</param>
    /// <param name="message">
    /// A human-readable detail. Falls back to <paramref name="code"/> when empty; never
    /// surfaced to the caller, which sees only the code.
    /// </param>
    /// <param name="innerException">The underlying validation failure, when there is one.</param>
    public TokenVerificationException(string code, string? message = null, Exception? innerException = null)
        : base(string.IsNullOrEmpty(message) ? code : message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the stable error code, one of the <see cref="OAuthErrorCodes"/> values.
    /// </summary>
    public string Code { get; }
}
