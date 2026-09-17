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
/// No source supplied the identity coordinates a verifier needs, so nothing can be verified.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="VerifierNotReadyException"/>, which is transient: this is a
/// deployment or configuration error that will not resolve on a retry. The middleware
/// answers 503 with <see cref="OAuthErrorCodes.NotConfigured"/> either way — a request the
/// app cannot adjudicate must not read as a rejected token.
/// </para>
/// <para>
/// The named type is what lets the middleware, and an app hosting its own
/// <see cref="ITokenVerifier"/>, catch this condition alone.
/// </para>
/// </remarks>
public sealed class IdentityNotConfiguredException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityNotConfiguredException"/> class.
    /// </summary>
    public IdentityNotConfiguredException()
        : base("Identity coordinates are not configured")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityNotConfiguredException"/> class.
    /// </summary>
    /// <param name="message">A human-readable detail naming the sources that were tried.</param>
    public IdentityNotConfiguredException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityNotConfiguredException"/> class.
    /// </summary>
    /// <param name="message">A human-readable detail naming the sources that were tried.</param>
    /// <param name="innerException">The underlying discovery failure.</param>
    public IdentityNotConfiguredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
