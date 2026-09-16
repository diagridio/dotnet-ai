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
/// JWKS key material has not loaded yet, so no verdict on the token is possible.
/// </summary>
/// <remarks>
/// Distinct from <see cref="TokenVerificationException"/>: the token may well be valid,
/// we simply cannot say. The middleware answers 503 rather than 401 so the caller retries
/// instead of discarding a good token.
/// </remarks>
public sealed class VerifierNotReadyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VerifierNotReadyException"/> class.
    /// </summary>
    public VerifierNotReadyException()
        : base("JWKS key material is not available")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifierNotReadyException"/> class.
    /// </summary>
    /// <param name="message">A human-readable detail.</param>
    public VerifierNotReadyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifierNotReadyException"/> class.
    /// </summary>
    /// <param name="message">A human-readable detail.</param>
    /// <param name="innerException">The underlying retrieval failure.</param>
    public VerifierNotReadyException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
