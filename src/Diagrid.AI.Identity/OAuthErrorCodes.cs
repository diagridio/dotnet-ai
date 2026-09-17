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
/// The error codes returned in the <c>error</c> field of a rejected request.
/// </summary>
/// <remarks>
/// These strings are part of the cross-SDK contract: a client that branches on
/// <c>oauth.missing_scope</c> must behave the same whichever SDK served the request.
/// Never rename one.
/// </remarks>
public static class OAuthErrorCodes
{
    /// <summary>No <c>X-Diagrid-User-Token</c> header on a request that requires one (401).</summary>
    public const string MissingToken = "oauth.missing_token";

    /// <summary>Identity coordinates could not be discovered, so nothing can be verified (503).</summary>
    public const string NotConfigured = "oauth.not_configured";

    /// <summary>
    /// The verifier cannot answer: key material is not loaded yet, or verification failed
    /// unexpectedly (503).
    /// </summary>
    public const string VerifierUnavailable = "oauth.verifier_unavailable";

    /// <summary>The token's <c>exp</c> has passed (401).</summary>
    public const string Expired = "oauth.expired";

    /// <summary>The token's <c>iss</c> does not match the configured issuer (401).</summary>
    public const string InvalidIssuer = "oauth.invalid_issuer";

    /// <summary>The token's <c>aud</c> does not match the configured audience (401).</summary>
    public const string InvalidAudience = "oauth.invalid_audience";

    /// <summary>The token's signature did not verify against the JWKS key material (401).</summary>
    public const string InvalidSignature = "oauth.invalid_signature";

    /// <summary>The token is not a well-formed JWT (401).</summary>
    public const string DecodeError = "oauth.decode_error";

    /// <summary>The token failed some other claim validation, such as a missing required claim (401).</summary>
    public const string InvalidToken = "oauth.invalid_token";

    /// <summary>The verified token does not carry every scope the route requires (403).</summary>
    public const string MissingScope = "oauth.missing_scope";
}
