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

using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// A throwaway RSA keypair plus the JWKS document that publishes its public half, so the
/// verifier can be exercised end to end without Auth0 or any live JWKS server.
/// </summary>
internal sealed class TestSigningKey : IDisposable
{
    internal const string DefaultKid = "test-key";

    private readonly RSA _rsa = RSA.Create(2048);

    internal TestSigningKey(string kid = DefaultKid) => Kid = kid;

    internal string Kid { get; }

    internal RsaSecurityKey SecurityKey => new(_rsa) { KeyId = Kid };

    /// <summary>
    /// Renders the public key as a JWKS document, the shape the verifier fetches.
    /// </summary>
    /// <returns>A JWKS JSON document holding this key.</returns>
    internal string ToJwksJson()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var modulus = Base64UrlEncoder.Encode(parameters.Modulus!);
        var exponent = Base64UrlEncoder.Encode(parameters.Exponent!);

        return $$"""
            {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{Kid}}","n":"{{modulus}}","e":"{{exponent}}"}]}
            """;
    }

    public void Dispose() => _rsa.Dispose();
}
