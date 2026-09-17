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
/// Policy the <see cref="OAuthMiddleware"/> enforces on every inbound request.
/// </summary>
/// <remarks>
/// Configured through <c>AddDiagridIdentity</c>, so the properties are settable: the instance
/// is mutated once at start-up by the configuration callback and read only thereafter.
/// </remarks>
public sealed record OAuthConfig
{
    /// <summary>
    /// Gets or sets the required scopes. The middleware returns 403 when the verified
    /// token lacks any of them.
    /// </summary>
    /// <remarks>
    /// A collection rather than the set <see cref="VerifiedUser.Scopes"/> exposes, because a
    /// C# collection expression cannot target <see cref="IReadOnlySet{T}"/>. Duplicates are
    /// harmless: the middleware folds this into a set at start-up.
    /// </remarks>
    public IReadOnlyCollection<string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the expected <c>iss</c> claim. Normally discovered from the sidecar
    /// <c>/v1.0/metadata</c> response; set explicitly only when the metadata endpoint is
    /// unavailable.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the expected <c>aud</c> claim. Same discovery rules as <see cref="Issuer"/>.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets the JWKS endpoint used for signature verification. Same discovery rules
    /// as <see cref="Issuer"/>.
    /// </summary>
    public string? JwksUri { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether inbound requests must carry a token.
    /// When <see langword="true"/> (default), requests without
    /// <c>X-Diagrid-User-Token</c> are rejected with 401. Set to <see langword="false"/>
    /// to allow unauthenticated routes (health, readiness) to share the same app.
    /// </summary>
    public bool RequireAuth { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a non-loopback <c>http://</c> JWKS endpoint is
    /// accepted. Off by default, and only loopback endpoints are exempt: signing keys fetched
    /// over plaintext can be substituted by anyone on the path, so turning this on gives up
    /// the guarantee that a verified token was signed by dp-Sentry.
    /// </summary>
    public bool AllowInsecureJwks { get; set; }
}
