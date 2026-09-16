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

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// Mints the tokens the verifier tests feed in, including the ones it must reject.
/// </summary>
internal static class TokenFactory
{
    internal const string Issuer = "https://oidc.example.com";
    internal const string JwksUri = "https://oidc.example.com/jwks.json";
    internal const string Subject = "alice@example.com";

    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>
    /// Signs an RS256 token with the supplied key.
    /// </summary>
    /// <param name="key">The signing key; its <c>kid</c> lands in the header.</param>
    /// <param name="issuer">The <c>iss</c> claim, or <see langword="null"/> to omit it.</param>
    /// <param name="audience">The <c>aud</c> claim, or <see langword="null"/> to omit it.</param>
    /// <param name="subject">The <c>sub</c> claim, or <see langword="null"/> to omit it.</param>
    /// <param name="expires">The <c>exp</c> claim; defaults to an hour out.</param>
    /// <param name="extraClaims">Additional claims to merge into the payload.</param>
    /// <returns>The signed compact JWT.</returns>
    internal static string Create(
        TestSigningKey key,
        string? issuer = Issuer,
        string? audience = null,
        string? subject = Subject,
        DateTime? expires = null,
        IDictionary<string, object>? extraClaims = null)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);
        if (subject is not null)
        {
            claims["sub"] = subject;
        }

        if (extraClaims is not null)
        {
            foreach (var (name, value) in extraClaims)
            {
                claims[name] = value;
            }
        }

        // Anchor iat/nbf an hour before exp rather than to "now", so an intentionally
        // expired token still describes a coherent lifetime and fails on expiry alone.
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        var issuedAt = expiry.AddHours(-1);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiry,
            SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256),
        };

        return Handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Signs an HS256 token, which the verifier's algorithm allowlist must reject however
    /// well-formed it is.
    /// </summary>
    /// <param name="kid">The <c>kid</c> header, matched against the JWKS.</param>
    /// <returns>The signed compact JWT.</returns>
    internal static string CreateHmacSigned(string kid)
    {
        var secret = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = kid };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = Subject },
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Hand-builds an <c>alg: none</c> token, which no signing library will produce and the
    /// verifier must refuse.
    /// </summary>
    /// <param name="kid">The <c>kid</c> header, matched against the JWKS.</param>
    /// <returns>The unsigned compact JWT, signature segment empty.</returns>
    internal static string CreateUnsigned(string kid)
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);

        var header = $$"""{"alg":"none","typ":"JWT","kid":"{{kid}}"}""";
        var payload = $$"""{"sub":"{{Subject}}","iss":"{{Issuer}}","exp":{{exp}}}""";

        return Base64UrlEncoder.Encode(header) + "." + Base64UrlEncoder.Encode(payload) + ".";
    }
}
