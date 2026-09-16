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
/// Verifies an inbound user token and returns its payload.
/// </summary>
/// <remarks>
/// Register an implementation before calling <c>AddDiagridIdentity</c> to take over
/// verification — the middleware uses whatever is in the container and only falls back to
/// building a <see cref="JwksVerifier"/> from discovered coordinates when nothing is
/// registered.
/// </remarks>
public interface ITokenVerifier
{
    /// <summary>
    /// Verifies signature and claims, returning the decoded payload.
    /// </summary>
    /// <param name="rawToken">The raw JWT, without the <c>Bearer </c> prefix.</param>
    /// <param name="cancellationToken">Cancels an in-flight JWKS fetch.</param>
    /// <returns>The verified claims.</returns>
    /// <exception cref="VerifierNotReadyException">Key material is unavailable.</exception>
    /// <exception cref="TokenVerificationException">Verification failed.</exception>
    Task<IReadOnlyDictionary<string, object>> VerifyAsync(
        string rawToken,
        CancellationToken cancellationToken = default);
}
