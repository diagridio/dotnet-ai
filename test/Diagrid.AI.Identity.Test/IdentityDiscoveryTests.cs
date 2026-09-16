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
using System.Net;
using Diagrid.AI.Identity.Test.TestUtilities;
using Microsoft.AspNetCore.TestHost;

namespace Diagrid.AI.Identity.Test;

/// <summary>
/// Covers coordinate discovery and the precedence <c>BuildAsync</c> applies to its sources.
/// Every test here mutates process-wide environment variables, which is why they share one
/// class: xUnit runs a class's methods serially.
/// </summary>
public sealed class IdentityDiscoveryTests
{
    private const string MetadataJson = """
        {"id":"test-app","identity":{"issuer":"https://oidc.test.com/org/region","jwks_uri":"https://oidc.test.com/org/region/jwks.json"}}
        """;

    /// <summary>A Catalyst project endpoint, the shape <c>diagrid dev run</c> exports.</summary>
    private const string RemoteEndpoint = "https://http-prj1.region:30443";

    /// <summary>A sidecar API token, the shape Catalyst issues.</summary>
    private const string ApiToken = "diagrid://v1/org/prj/token";

    [Fact]
    public void DiscoverFromEnvironment_DerivesJwksUriFromIssuer()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.IssuerVariable, "https://oidc.test.com/org/region"));

        var coordinates = IdentityDiscovery.DiscoverFromEnvironment();

        Assert.NotNull(coordinates);
        Assert.Equal("https://oidc.test.com/org/region", coordinates.Issuer);
        Assert.Equal("https://oidc.test.com/org/region/jwks.json", coordinates.JwksUri);
    }

    [Fact]
    public void DiscoverFromEnvironment_TrimsTrailingSlashBeforeAppendingJwksPath()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.IssuerVariable, "https://oidc.test.com/org/region/"));

        var coordinates = IdentityDiscovery.DiscoverFromEnvironment();

        Assert.NotNull(coordinates);
        Assert.Equal("https://oidc.test.com/org/region/jwks.json", coordinates.JwksUri);
    }

    [Fact]
    public void DiscoverFromEnvironment_WithoutIssuer_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope();

        Assert.Null(IdentityDiscovery.DiscoverFromEnvironment());
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_ReadsTheIdentityBlock()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, "3500"));
        var handler = StubHttpMessageHandler.Json(MetadataJson);
        using var client = new HttpClient(handler);

        var coordinates = await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);
        Assert.Equal("https://oidc.test.com/org/region", coordinates.Issuer);
        Assert.Equal("http://127.0.0.1:3500/v1.0/metadata", Assert.Single(handler.RequestedUris));
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_PrefersTheCatalystPortVariable()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.CatalystDaprHttpPortVariable, "3600"),
            (IdentityDiscovery.DaprHttpPortVariable, "3500"));
        var handler = StubHttpMessageHandler.Json(MetadataJson);
        using var client = new HttpClient(handler);

        await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("http://127.0.0.1:3600/v1.0/metadata", Assert.Single(handler.RequestedUris));
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_WithoutPort_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope();
        var handler = StubHttpMessageHandler.Json(MetadataJson);
        using var client = new HttpClient(handler);

        var coordinates = await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
        Assert.Empty(handler.RequestedUris);
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_WithoutIdentityBlock_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, "3500"));
        using var client = new HttpClient(StubHttpMessageHandler.Json("""{"id":"test-app"}"""));

        var coordinates = await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_WhenTheProbeFails_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, "3500"));
        using var client = new HttpClient(StubHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var coordinates = await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
    }

    [Fact]
    public async Task DiscoverFromMetadataAsync_WhenTheProbeFails_WarnsAboutTheNextSource()
    {
        // A local probe that fails is how a `diagrid dev run` app ends up on the remote
        // sidecar, so an operator has to be able to see it happen.
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, "3500"));
        using var client = new HttpClient(
            new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused")));
        var logger = new RecordingLogger();

        var coordinates = await IdentityDiscovery.DiscoverFromMetadataAsync(
            client,
            logger,
            TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("http://127.0.0.1:3500/v1.0/metadata", warning, StringComparison.Ordinal);
        Assert.Contains("HttpRequestException: connection refused", warning, StringComparison.Ordinal);
        Assert.Contains("trying the next source", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_ReadsTheIdentityBlock()
    {
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        sidecar.PublishIdentity(
            "https://oidc.test.com/org/region",
            "https://oidc.test.com/org/region/jwks.json");

        // Trailing slash included: the endpoint and the path must not both contribute one.
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, sidecar.Origin + "/"));
        using var client = new HttpClient();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);
        Assert.Equal("https://oidc.test.com/org/region", coordinates.Issuer);
        Assert.Equal("https://oidc.test.com/org/region/jwks.json", coordinates.JwksUri);
        Assert.Equal("/v1.0/metadata", Assert.Single(sidecar.Requests).Path);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WithoutEndpoint_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope();
        var handler = StubHttpMessageHandler.Json(MetadataJson);
        using var client = new HttpClient(handler);

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
        Assert.Empty(handler.RequestedUris);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_SendsTheApiTokenHeader()
    {
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        sidecar.PublishIdentity("https://oidc.test.com/org/region");
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, sidecar.Origin),
            (IdentityDiscovery.DaprApiTokenVariable, ApiToken));
        using var client = new HttpClient();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);

        // Asserted on what the sidecar received, not on what the client thought it sent.
        Assert.Equal(ApiToken, Assert.Single(sidecar.Requests).ApiToken);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WithoutApiToken_OmitsTheHeader()
    {
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        sidecar.PublishIdentity("https://oidc.test.com/org/region");
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, sidecar.Origin));
        using var client = new HttpClient();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);
        Assert.Null(Assert.Single(sidecar.Requests).ApiToken);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WhenTheProbeFails_ReturnsNull()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, RemoteEndpoint));
        using var client = new HttpClient(
            StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WhenUnreachable_WarnsAboutTheNextSource()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, RemoteEndpoint),
            (IdentityDiscovery.DaprApiTokenVariable, ApiToken));
        using var client = new HttpClient(
            new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused")));
        var logger = new RecordingLogger();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            logger,
            TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains(RemoteEndpoint + "/v1.0/metadata", warning, StringComparison.Ordinal);
        Assert.Contains("HttpRequestException: connection refused", warning, StringComparison.Ordinal);
        Assert.Contains("trying the next source", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WithoutEndpoint_LogsNothing()
    {
        // An unconfigured source is not a failed one: an in-cluster app must not warn about
        // a source it never had.
        using var environment = new EnvironmentVariableScope();
        using var client = new HttpClient(StubHttpMessageHandler.Json(MetadataJson));
        var logger = new RecordingLogger();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            logger,
            TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
        Assert.Empty(logger.Records);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WithApiTokenOverPlainHttp_WarnsAndStillSendsIt()
    {
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        sidecar.PublishIdentity("https://oidc.test.com/org/region");
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, sidecar.Origin),
            (IdentityDiscovery.DaprApiTokenVariable, ApiToken));
        using var client = new HttpClient();
        var logger = new RecordingLogger();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            logger,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("clear text", warning, StringComparison.Ordinal);
        Assert.Contains(sidecar.Origin, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, warning, StringComparison.Ordinal);

        // Warned, not refused: a self-hosted sidecar on plain http is a valid setup.
        Assert.Equal(ApiToken, Assert.Single(sidecar.Requests).ApiToken);
    }

    [Fact]
    public async Task DiscoverFromRemoteAsync_WithApiTokenOverHttps_DoesNotWarn()
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, RemoteEndpoint),
            (IdentityDiscovery.DaprApiTokenVariable, ApiToken));
        using var client = new HttpClient(StubHttpMessageHandler.Json(MetadataJson));
        var logger = new RecordingLogger();

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            logger,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coordinates);
        Assert.Empty(logger.Records);
    }

    [Theory]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""{"identity":{"issuer":123}}""")]
    [InlineData("""{"identity":"not-an-object"}""")]
    [InlineData("not json at all")]
    public async Task DiscoverFromRemoteAsync_WithMalformedBody_ReturnsNull(string body)
    {
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, RemoteEndpoint));
        using var client = new HttpClient(StubHttpMessageHandler.Json(body));

        var coordinates = await IdentityDiscovery.DiscoverFromRemoteAsync(
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(coordinates);
    }

    [Fact]
    public async Task BuildAsync_PrefersTheLocalSidecarOverTheRemoteOne()
    {
        // An in-cluster app keeps its loopback call rather than paying for a network round
        // trip, so the remote endpoint must not even be asked.
        using var key = new TestSigningKey();
        await using var local = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        await using var remote = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        local.PublishIdentity(local.Origin, local.JwksUri);
        local.JwksJson = key.ToJwksJson();
        remote.PublishIdentity(remote.Origin, remote.JwksUri);
        remote.JwksJson = key.ToJwksJson();

        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, local.Port.ToString(CultureInfo.InvariantCulture)),
            (IdentityDiscovery.DaprHttpEndpointVariable, remote.Origin));
        using var client = new HttpClient();

        var verifier = await JwksVerifier.BuildAsync(
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key, issuer: local.Origin),
            TestContext.Current.CancellationToken);

        Assert.Equal(local.Origin, claims["iss"]);
        Assert.Empty(remote.Requests);
    }

    [Fact]
    public async Task BuildAsync_HonoursAJwksUriTheSidecarPublishesAwayFromItsIssuer()
    {
        // A sidecar that publishes a jwks_uri away from its issuer means it: deriving
        // issuer + /jwks.json over the top points the verifier at an endpoint that need not
        // exist, after which every request answers 503 oauth.verifier_unavailable.
        using var key = new TestSigningKey();
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        await using var keys = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        sidecar.PublishIdentity(sidecar.Origin, keys.JwksUri);
        keys.JwksJson = key.ToJwksJson();

        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, sidecar.Port.ToString(CultureInfo.InvariantCulture)));
        using var client = new HttpClient();

        var verifier = await JwksVerifier.BuildAsync(
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key, issuer: sidecar.Origin),
            TestContext.Current.CancellationToken);

        Assert.Equal(sidecar.Origin, claims["iss"]);
        Assert.Contains(
            IdentityDiscovery.JwksPathSuffix,
            keys.Requests.Select(request => request.Path),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_PinnedIssuerIgnoresAForeignJwksUri()
    {
        // The discovered jwks_uri is adopted only when the discovered issuer is the one that
        // resolved: otherwise a token minted by the advertised issuer but claiming the pinned
        // one would verify.
        using var pinnedKey = new TestSigningKey("pinned-key");
        using var foreignKey = new TestSigningKey("foreign-key");
        await using var pinned = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        await using var sidecar = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        pinned.JwksJson = pinnedKey.ToJwksJson();
        sidecar.PublishIdentity(sidecar.Origin, sidecar.JwksUri);
        sidecar.JwksJson = foreignKey.ToJwksJson();

        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpPortVariable, sidecar.Port.ToString(CultureInfo.InvariantCulture)));
        using var client = new HttpClient();

        var verifier = await JwksVerifier.BuildAsync(
            issuer: pinned.Origin,
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(pinnedKey, issuer: pinned.Origin),
            TestContext.Current.CancellationToken);

        Assert.Equal(pinned.Origin, claims["iss"]);

        Assert.DoesNotContain(
            IdentityDiscovery.JwksPathSuffix,
            sidecar.Requests.Select(request => request.Path),
            StringComparer.Ordinal);
        await Assert.ThrowsAsync<VerifierNotReadyException>(
            () => verifier.VerifyAsync(
                TokenFactory.Create(foreignKey, issuer: pinned.Origin),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_FallsBackToTheRemoteSidecarWhenThereIsNoLocalOne()
    {
        using var key = new TestSigningKey();
        await using var remote = await LoopbackMetadataServer.StartAsync(
            TestContext.Current.CancellationToken);
        remote.PublishIdentity(remote.Origin, remote.JwksUri);
        remote.JwksJson = key.ToJwksJson();

        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, remote.Origin));
        using var client = new HttpClient();

        var verifier = await JwksVerifier.BuildAsync(
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key, issuer: remote.Origin),
            TestContext.Current.CancellationToken);

        Assert.Equal(remote.Origin, claims["iss"]);
        Assert.Contains(
            IdentityDiscovery.MetadataPath,
            remote.Requests.Select(request => request.Path),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_WhenTheRemoteSidecarFails_FallsBackToEnvironment()
    {
        using var key = new TestSigningKey();
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.DaprHttpEndpointVariable, RemoteEndpoint),
            (IdentityDiscovery.IssuerVariable, TokenFactory.Issuer));
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == IdentityDiscovery.MetadataPath
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : StubHttpMessageHandler.JsonResponse(key.ToJwksJson())));

        var verifier = await JwksVerifier.BuildAsync(
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key),
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Issuer, claims["iss"]);
    }

    [Fact]
    public async Task BuildAsync_WithoutAnySource_ThrowsIdentityNotConfigured()
    {
        // A named type is what lets the middleware — and an app hosting its own verifier —
        // tell "nothing is configured" from any other failure.
        using var environment = new EnvironmentVariableScope();
        using var client = new HttpClient(StubHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<IdentityNotConfiguredException>(
            () => JwksVerifier.BuildAsync(
                httpClient: client,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Cannot discover", exception.Message, StringComparison.Ordinal);

        // The message has to name both ways to reach a sidecar, local and remote.
        Assert.Contains(IdentityDiscovery.DaprHttpPortVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(IdentityDiscovery.DaprHttpEndpointVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(IdentityDiscovery.IssuerVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Middleware_WithoutDiscoverableCoordinates_IsServiceUnavailable()
    {
        using var environment = new EnvironmentVariableScope();
        using var host = await IdentityTestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/invoke", UriKind.Relative));
        request.Headers.Add(IdentityContext.UserTokenHeader, "Bearer fake.jwt.token");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("oauth.not_configured", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_WithIssuerOnly_DerivesTheJwksEndpoint()
    {
        using var environment = new EnvironmentVariableScope();
        using var key = new TestSigningKey();
        var handler = StubHttpMessageHandler.Json(key.ToJwksJson());
        using var client = new HttpClient(handler);

        var verifier = await JwksVerifier.BuildAsync(
            issuer: TokenFactory.Issuer,
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key),
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Subject, claims["sub"]);
        Assert.Contains(TokenFactory.JwksUri, handler.RequestedUris, StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_FallsBackToEnvironmentWhenMetadataIsUnavailable()
    {
        using var key = new TestSigningKey();
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.IssuerVariable, TokenFactory.Issuer));
        var handler = StubHttpMessageHandler.Json(key.ToJwksJson());
        using var client = new HttpClient(handler);

        var verifier = await JwksVerifier.BuildAsync(
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key),
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Issuer, claims["iss"]);
    }

    [Fact]
    public async Task BuildAsync_ExplicitCoordinatesBeatDiscovery()
    {
        using var key = new TestSigningKey();
        using var environment = new EnvironmentVariableScope(
            (IdentityDiscovery.IssuerVariable, "https://discovered.example.com"));
        var handler = StubHttpMessageHandler.Json(key.ToJwksJson());
        using var client = new HttpClient(handler);

        var verifier = await JwksVerifier.BuildAsync(
            issuer: TokenFactory.Issuer,
            jwksUri: TokenFactory.JwksUri,
            httpClient: client,
            cancellationToken: TestContext.Current.CancellationToken);

        var claims = await verifier.VerifyAsync(
            TokenFactory.Create(key),
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenFactory.Issuer, claims["iss"]);
    }
}
