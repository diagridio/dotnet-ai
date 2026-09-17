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

using System.Net;
using Diagrid.AI.Identity.Test.TestUtilities;

namespace Diagrid.AI.Identity.Test;

public sealed class JwksVerifierTests : IDisposable
{
    private readonly TestSigningKey _key = new();
    private readonly List<HttpClient> _clients = [];
    private readonly List<JwksVerifier> _verifiers = [];

    public void Dispose()
    {
        foreach (var verifier in _verifiers)
        {
            verifier.Dispose();
        }

        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _key.Dispose();
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsClaims()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key);

        var claims = await verifier.VerifyAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Subject, claims["sub"]);
        Assert.Equal(TokenFactory.Issuer, claims["iss"]);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_ReportsExpired()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, expires: DateTime.UtcNow.AddHours(-1));

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.expired", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_TokenExpiredWithinClockSkew_IsAccepted()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(
            _key,
            expires: DateTime.UtcNow.AddSeconds(-(JwksVerifier.ClockSkewSeconds / 2)));

        var claims = await verifier.VerifyAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Subject, claims["sub"]);
    }

    [Fact]
    public async Task VerifyAsync_WrongIssuer_ReportsInvalidIssuer()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, issuer: "https://wrong-issuer.com");

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_issuer", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_WrongAudience_ReportsInvalidAudience()
    {
        var verifier = CreateVerifier(audience: "expected-audience");
        var token = TokenFactory.Create(_key, audience: "some-other-audience");

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_audience", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_WrongIssuerAndWrongAudience_ReportsInvalidIssuer()
    {
        // The claim order is required claims, then exp, then iss, then aud, so a token with
        // two defects always reports the same code. Microsoft.IdentityModel validates
        // lifetime, audience, then issuer, so the issuer is re-checked by hand before an
        // audience failure is mapped.
        var verifier = CreateVerifier(audience: "expected-audience");
        var token = TokenFactory.Create(
            _key,
            issuer: "https://wrong-issuer.com",
            audience: "some-other-audience");

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_issuer", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredAndWrongIssuer_ReportsExpired()
    {
        // exp precedes iss.
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(
            _key,
            issuer: "https://wrong-issuer.com",
            expires: DateTime.UtcNow.AddHours(-1));

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.expired", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredAndWrongAudience_ReportsExpired()
    {
        // exp precedes aud.
        var verifier = CreateVerifier(audience: "expected-audience");
        var token = TokenFactory.Create(
            _key,
            audience: "some-other-audience",
            expires: DateTime.UtcNow.AddHours(-1));

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.expired", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_MissingSubjectAndWrongIssuer_ReportsInvalidToken()
    {
        // The required-claims check precedes every other claim check, so a token missing one
        // of exp/iss/sub reports invalid_token whatever else is wrong with it.
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, issuer: "https://wrong-issuer.com", subject: null);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_token", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_NoConfiguredAudience_SkipsAudienceValidation()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, audience: "anything-at-all");

        var claims = await verifier.VerifyAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Subject, claims["sub"]);
    }

    [Fact]
    public async Task VerifyAsync_SignedByAnotherKey_ReportsInvalidSignature()
    {
        // Same kid, different key material: the verifier finds a key to try and it fails,
        // which is a rejection rather than a "key material is stale" condition.
        using var impostor = new TestSigningKey();
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(impostor);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_signature", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_MalformedToken_ReportsDecodeError()
    {
        var verifier = CreateVerifier();

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync("not-a-jwt", TestContext.Current.CancellationToken));

        Assert.Equal("oauth.decode_error", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_HmacSignedToken_IsRejectedByAlgorithmAllowlist()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.CreateHmacSigned(_key.Kid);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_token", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_UnsignedToken_IsRejectedByAlgorithmAllowlist()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.CreateUnsigned(_key.Kid);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_token", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_MissingSubjectClaim_ReportsInvalidToken()
    {
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, subject: null);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_token", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_UnknownKid_ReportsVerifierNotReady()
    {
        // Our key material cannot speak to this token, so the honest answer is "ask again",
        // not "you are forged".
        using var unknown = new TestSigningKey("some-other-kid");
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(unknown);

        await Assert.ThrowsAsync<VerifierNotReadyException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyAsync_JwksUnavailable_ReportsVerifierNotReady()
    {
        var verifier = CreateVerifier(StubHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable));
        var token = TokenFactory.Create(_key);

        await Assert.ThrowsAsync<VerifierNotReadyException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WarmAsync_SwallowsFetchFailures()
    {
        var verifier = CreateVerifier(StubHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable));

        await verifier.WarmAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void CacheAndSkewConstants_MatchTheCrossSdkContract()
    {
        Assert.Equal(120, JwksVerifier.ClockSkewSeconds);
        Assert.Equal(300, JwksVerifier.JwksCacheLifetimeSeconds);
    }

    [Fact]
    public void Constructor_PlaintextJwksUri_IsRefusedAsNotConfigured()
    {
        // Signing-key material fetched over plaintext can be substituted by anyone on the
        // path, so a public http:// endpoint is a configuration error, not a degraded mode.
        // Reported as the not-configured failure rather than ArgumentException, which the
        // middleware maps to 503 oauth.not_configured.
        var exception = Assert.Throws<IdentityNotConfiguredException>(
            () => new JwksVerifier(TokenFactory.Issuer, "http://oidc.example.com/jwks.json"));

        Assert.Contains("must use HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_UnparseableJwksUri_IsRefusedAsNotConfigured()
    {
        var exception = Assert.Throws<IdentityNotConfiguredException>(
            () => new JwksVerifier(TokenFactory.Issuer, "not a uri"));

        Assert.Contains("absolute URI", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_EmptyJwksUri_IsACallerContractViolation()
    {
        // The one case that stays ArgumentException: no configuration source can produce an
        // empty endpoint, so this is the caller passing nothing.
        var exception = Assert.Throws<ArgumentException>(
            () => new JwksVerifier(TokenFactory.Issuer, string.Empty));

        Assert.Equal("jwksUri", exception.ParamName);
    }

    [Theory]
    [InlineData("file:///etc/diagrid/jwks.json")]
    [InlineData("ldap://directory.example.com/jwks")]
    public void Constructor_NonHttpJwksUri_IsRefusedEvenWhenTheAppOptsIn(string jwksUri)
    {
        // AllowInsecureJwks relaxes plain HTTP and nothing else — and a file:// URI has an
        // empty host, which Uri.IsLoopback reports as loopback.
        var exception = Assert.Throws<IdentityNotConfiguredException>(
            () => new JwksVerifier(TokenFactory.Issuer, jwksUri, allowInsecureJwks: true));

        Assert.Contains("relaxes plain HTTP only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_PlaintextLoopbackJwksUri_IsAllowed()
    {
        // The local sidecar advertises a plain-HTTP issuer; nothing on the path to
        // 127.0.0.1 can substitute the key set.
        using var verifier = new JwksVerifier(TokenFactory.Issuer, "http://127.0.0.1:3500/jwks.json");

        Assert.NotNull(verifier);
    }

    [Fact]
    public void Constructor_PlaintextJwksUri_IsAllowedWhenTheAppOptsIn()
    {
        using var verifier = new JwksVerifier(
            TokenFactory.Issuer,
            "http://oidc.example.com/jwks.json",
            allowInsecureJwks: true);

        Assert.NotNull(verifier);
    }

    [Fact]
    public async Task VerifyAsync_RotatedSigningKey_IsAbsorbedWithinTheRequest()
    {
        // A rotated key is stale cache, not a bad token: the verifier re-fetches rather than
        // serving 503 until the automatic refresh interval elapses.
        using var rotated = new TestSigningKey("rotated-key");
        var handler = StubHttpMessageHandler.Json(_key.ToJwksJson());
        var verifier = CreateVerifier(handler);

        await verifier.VerifyAsync(TokenFactory.Create(_key), TestContext.Current.CancellationToken);

        handler.JsonBody = rotated.ToJwksJson();
        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(rotated),
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Subject, claims["sub"]);
    }

    [Fact]
    public async Task VerifyAsync_RepeatedUnknownKid_DoesNotRefetchPerRequest()
    {
        // Re-fetching on an unrecognised `kid` is what absorbs a rotation, so it must be
        // rate-limited: otherwise a stream of forged `kid`s turns into a stream of JWKS
        // fetches aimed at dp-Sentry.
        const int Attempts = 10;
        const int MaxFetches = 4;
        using var unknown = new TestSigningKey("some-other-kid");
        var handler = StubHttpMessageHandler.Json(_key.ToJwksJson());
        var verifier = CreateVerifier(handler);
        var token = TokenFactory.Create(unknown);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            await Assert.ThrowsAsync<VerifierNotReadyException>(
                () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));
        }

        // Let the refresh the configuration manager runs in the background land first.
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.True(
            handler.RequestedUris.Count <= MaxFetches,
            $"{Attempts} unknown-kid verifications caused {handler.RequestedUris.Count} JWKS fetches");
    }

    [Fact]
    public async Task Dispose_ReleasesTheRefreshGate()
    {
        using var unknown = new TestSigningKey("some-other-kid");
        var verifier = CreateVerifier();
        await verifier.VerifyAsync(TokenFactory.Create(_key), TestContext.Current.CancellationToken);

        verifier.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => verifier.VerifyAsync(
                TokenFactory.Create(unknown),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyAsync_MissingIssuerClaim_ReportsInvalidToken()
    {
        // The required claims are checked before the issuer, so a token with no "iss" is a
        // missing-claim failure rather than an issuer mismatch.
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key, issuer: null);

        var exception = await Assert.ThrowsAsync<TokenVerificationException>(
            () => verifier.VerifyAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal("oauth.invalid_token", exception.Code);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsAViewIndependentOfTheValidatorsDictionary()
    {
        // TokenValidationResult.Claims is declared IDictionary<string, object>; depending on
        // its runtime type is what turns a library update into a 500 on every success.
        var verifier = CreateVerifier();
        var token = TokenFactory.Create(_key);

        var claims = await verifier.VerifyAsync(token, TestContext.Current.CancellationToken);

        Assert.IsNotType<Dictionary<string, object>>(claims);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, object>)claims).Add("injected", "value"));
    }

    [Fact]
    public void SharedHttpClient_RecyclesPooledConnections()
    {
        // Without a pooled-connection lifetime the process-wide client never observes a DNS
        // change for the JWKS host.
        using var handler = JwksVerifier.CreateSharedHandler();

        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);
    }

    private JwksVerifier CreateVerifier(string audience = "") =>
        CreateVerifier(StubHttpMessageHandler.Json(_key.ToJwksJson()), audience);

    private JwksVerifier CreateVerifier(StubHttpMessageHandler handler, string audience = "")
    {
        var client = new HttpClient(handler);
        _clients.Add(client);
        var verifier = new JwksVerifier(TokenFactory.Issuer, TokenFactory.JwksUri, audience, client);
        _verifiers.Add(verifier);
        return verifier;
    }
}
