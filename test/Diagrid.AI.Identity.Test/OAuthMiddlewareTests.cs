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
using System.Net.Http.Json;
using System.Text.Json;
using Diagrid.AI.Identity.Test.TestUtilities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Identity.Test;

public sealed class OAuthMiddlewareTests
{
    private const string SomeToken = "Bearer fake.jwt.token";

    [Fact]
    public async Task ValidToken_ExposesTheVerifiedUser()
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "alice@example.com",
            ["tid"] = "acme-corp",
            ["scp"] = new[] { "agent.invoke", "admin" },
            ["iss"] = TokenFactory.Issuer,
        };
        using var host = await IdentityTestServer.StartAsync(
            config => config.Scopes = ["agent.invoke"],
            FakeTokenVerifier.Accepting(claims),
            WriteVerifiedUser);
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await ReadUserAsync(response);
        Assert.Equal("alice@example.com", user.Subject);
        Assert.Equal("acme-corp", user.Tenant);
        Assert.Contains("agent.invoke", user.Scopes);
        Assert.Equal(TokenFactory.Issuer, user.IssuerId);
    }

    [Fact]
    public async Task MissingToken_IsRejected()
    {
        using var host = await IdentityTestServer.StartAsync(verifier: FakeTokenVerifier.Accepting());
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            new Uri("/invoke", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("oauth.missing_token", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task MissingToken_IsAllowedWhenAuthIsNotRequired()
    {
        using var host = await IdentityTestServer.StartAsync(
            config => config.RequireAuth = false,
            FakeTokenVerifier.Accepting());
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationHeader_IsIgnored()
    {
        using var host = await IdentityTestServer.StartAsync(verifier: FakeTokenVerifier.Accepting());
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer some.jwt");

        var response = await client.GetAsync(
            new Uri("/invoke", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("oauth.missing_token", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(
                new TokenVerificationException(OAuthErrorCodes.InvalidSignature)));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("oauth.invalid_signature", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task MissingScope_IsForbidden()
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "bob@example.com",
            ["scp"] = new[] { "agent.invoke" },
            ["iss"] = TokenFactory.Issuer,
        };
        using var host = await IdentityTestServer.StartAsync(
            config => config.Scopes = ["admin.write"],
            FakeTokenVerifier.Accepting(claims));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("oauth.missing_scope", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task MissingScopeFromTheVerifier_IsAlsoForbidden()
    {
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(
                new TokenVerificationException(OAuthErrorCodes.MissingScope)));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("oauth.missing_scope", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task VerifierNotReady_IsServiceUnavailable()
    {
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(new VerifierNotReadyException("JWKS loading")));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("oauth.verifier_unavailable", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task PlaintextJwksUri_IsServiceUnavailableAndStaysThatWay()
    {
        // A JWKS endpoint the verifier refuses is a configuration outcome, so it reaches the
        // wire as 503 oauth.not_configured rather than as an unhandled exception. The build
        // is cached, so the answer is asserted twice.
        using var host = await IdentityTestServer.StartAsync(config =>
        {
            config.Issuer = TokenFactory.Issuer;
            config.JwksUri = "http://oidc.example.com/jwks.json";
        });
        using var client = host.GetTestClient();

        var first = await SendWithTokenAsync(client);
        var second = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal("oauth.not_configured", await ReadErrorAsync(first));
        Assert.True(first.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("oauth.not_configured", await ReadErrorAsync(second));
        Assert.True(second.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task UnexpectedVerifierBuildFailure_IsServiceUnavailable()
    {
        // Fail closed on anything the build throws, not only on the not-configured type.
        // Driven through the provider's build seam because no configuration reaches this:
        // every refusal the build makes is already the not-configured type.
        var provider = new TokenVerifierProvider(
            () => throw new NotSupportedException("the key store went away"));
        using var host = await IdentityTestServer.StartAsync(
            configureServices: services => services.AddSingleton(provider));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("oauth.not_configured", await ReadErrorAsync(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task VerifierTimeout_IsServiceUnavailable_NotAClientDisconnect()
    {
        // A verifier's own HTTP timeout surfaces as TaskCanceledException, the same type a
        // client disconnect raises. Filtering on the type alone let it escape to the pipeline;
        // only a signalled RequestAborted means the caller actually went away.
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(new TaskCanceledException("jwks fetch timed out")));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("oauth.verifier_unavailable", await ReadErrorAsync(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task UnexpectedVerifierFailure_IsServiceUnavailable()
    {
        // Driven through the middleware rather than the verifier: the defect is an exception
        // escaping into the ASP.NET pipeline, where it becomes a 500 with a framework-shaped
        // body and no Cache-Control.
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(new InvalidOperationException("the key store went away")));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("oauth.verifier_unavailable", await ReadErrorAsync(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RejectedToken_KeepsItsOwnCodeAlongsideTheBroadCatch()
    {
        // The regression the broad catch could cause: ordered first, it would swallow
        // TokenVerificationException and answer 503 for a token that was correctly rejected.
        using var expiredHost = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(new TokenVerificationException(OAuthErrorCodes.Expired)));
        using var expiredClient = expiredHost.GetTestClient();

        var expired = await SendWithTokenAsync(expiredClient);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("oauth.expired", await ReadErrorAsync(expired));

        using var scopeHost = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Failing(new TokenVerificationException(OAuthErrorCodes.MissingScope)));
        using var scopeClient = scopeHost.GetTestClient();

        var missingScope = await SendWithTokenAsync(scopeClient);

        Assert.Equal(HttpStatusCode.Forbidden, missingScope.StatusCode);
        Assert.Equal("oauth.missing_scope", await ReadErrorAsync(missingScope));

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "bob@example.com",
            ["scp"] = new[] { "agent.invoke" },
            ["iss"] = TokenFactory.Issuer,
        };
        using var policyHost = await IdentityTestServer.StartAsync(
            config => config.Scopes = ["admin.write"],
            FakeTokenVerifier.Accepting(claims));
        using var policyClient = policyHost.GetTestClient();

        var policyDenied = await SendWithTokenAsync(policyClient);

        Assert.Equal(HttpStatusCode.Forbidden, policyDenied.StatusCode);
        Assert.Equal("oauth.missing_scope", await ReadErrorAsync(policyDenied));
    }

    [Fact]
    public async Task ClientDisconnect_IsLeftToTheFramework()
    {
        // A real disconnect has to reach ASP.NET; turning it into a 503 would report an outage
        // for a request nobody is waiting on. The verifier waits on RequestAborted rather than
        // throwing, so cancelling the caller signals the token the middleware filters on.
        // Asserted on the server, not on the caller: SendAsync throws on the client side
        // whatever the server does, so only the absence of the failure warning shows the abort
        // was left alone rather than adjudicated into a 503.
        var logger = new RecordingLogger();
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.WaitingForCancellation(),
            configureServices: services => services.AddSingleton<ILogger<OAuthMiddleware>>(
                new RecordingLogger<OAuthMiddleware>(logger)));
        using var client = host.GetTestClient();
        using var caller = new CancellationTokenSource();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/invoke", UriKind.Relative));
        request.Headers.Add(IdentityContext.UserTokenHeader, SomeToken);
        var send = client.SendAsync(request, caller.Token);
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.DoesNotContain(
            logger.Warnings,
            warning => warning.Contains("unexpected identity verification failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErrorResponses_AreNotCacheable()
    {
        using var host = await IdentityTestServer.StartAsync(verifier: FakeTokenVerifier.Accepting());
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            new Uri("/invoke", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task OutboundToken_IsAvailableDuringTheRequestAndRestoredAfter()
    {
        string? duringRequest = null;
        string? afterRequest = "sentinel";
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(),
            handler: context =>
            {
                duringRequest = IdentityContext.CurrentUserToken;
                return context.Response.WriteAsync("ok");
            },
            onRequestCompleted: () => afterRequest = IdentityContext.CurrentUserToken);
        using var client = host.GetTestClient();

        await SendWithTokenAsync(client, "Bearer the.raw.token");

        Assert.Equal("the.raw.token", duringRequest);
        Assert.Null(afterRequest);
    }

    [Fact]
    public async Task Scopes_AreReadFromASpaceDelimitedString()
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "alice",
            ["scope"] = "read write",
            ["iss"] = TokenFactory.Issuer,
        };
        using var host = await IdentityTestServer.StartAsync(
            config => config.Scopes = ["read"],
            FakeTokenVerifier.Accepting(claims),
            WriteVerifiedUser);
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await ReadUserAsync(response);
        Assert.Equal(["read", "write"], user.Scopes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tenant_FallsBackToTheUnprefixedClaim()
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "alice",
            ["tenant"] = "acme-corp",
            ["iss"] = TokenFactory.Issuer,
        };
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(claims),
            handler: WriteVerifiedUser);
        using var client = host.GetTestClient();

        var user = await ReadUserAsync(await SendWithTokenAsync(client));

        Assert.Equal("acme-corp", user.Tenant);
    }

    [Fact]
    public async Task VerifiedUser_LandsOnTheNamespacedItemKey()
    {
        object? item = null;
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(),
            handler: context =>
            {
                item = context.Items["diagrid.user"];
                return context.Response.WriteAsync("ok");
            });
        using var client = host.GetTestClient();

        await SendWithTokenAsync(client);

        var user = Assert.IsType<VerifiedUser>(item);
        Assert.Equal(TokenFactory.Subject, user.Subject);
    }

    [Fact]
    public async Task GetVerifiedUser_ReturnsTheVerifiedCaller()
    {
        VerifiedUser? user = null;
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(),
            handler: context =>
            {
                user = context.GetVerifiedUser();
                return context.Response.WriteAsync("ok");
            });
        using var client = host.GetTestClient();

        await SendWithTokenAsync(client);

        Assert.NotNull(user);
        Assert.Equal(TokenFactory.Subject, user.Subject);
    }

    [Fact]
    public async Task GetVerifiedUser_OnAnUnauthenticatedRequest_ReturnsNull()
    {
        var called = false;
        VerifiedUser? user = null;
        using var host = await IdentityTestServer.StartAsync(
            config => config.RequireAuth = false,
            handler: context =>
            {
                called = true;
                user = context.GetVerifiedUser();
                return context.Response.WriteAsync("ok");
            });
        using var client = host.GetTestClient();

        await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(called);
        Assert.Null(user);
    }

    [Fact]
    public async Task Scopes_EnumerateInOrdinalOrderWhateverOrderTheTokenUsed()
    {
        // A handler echoing Scopes into JSON must produce one stable order, so it cannot
        // come from a hash set's bucket layout.
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "alice",
            ["scp"] = new[] { "zeta", "alpha", "mid" },
            ["iss"] = TokenFactory.Issuer,
        };
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(claims),
            handler: WriteVerifiedUser);
        using var client = host.GetTestClient();

        var user = await ReadUserAsync(await SendWithTokenAsync(client));

        Assert.Equal(["alpha", "mid", "zeta"], user.Scopes);
    }

    [Fact]
    public async Task HttpContextUser_IsLeftToTheApplication()
    {
        // The app's own authentication owns HttpContext.User; writing it is the collision
        // the namespaced Items key exists to avoid.
        var identityCount = -1;
        using var host = await IdentityTestServer.StartAsync(
            verifier: FakeTokenVerifier.Accepting(),
            handler: context =>
            {
                identityCount = context.User.Identities.Count(identity => identity.IsAuthenticated);
                return context.Response.WriteAsync("ok");
            });
        using var client = host.GetTestClient();

        await SendWithTokenAsync(client);

        Assert.Equal(0, identityCount);
    }

    [Fact]
    public async Task UnauthenticatedRequest_LeavesTheItemKeyAbsent()
    {
        // Downstream code tells "no verified caller" from the key being absent, so a null
        // sentinel would break the check.
        var present = true;
        using var host = await IdentityTestServer.StartAsync(
            config => config.RequireAuth = false,
            handler: context =>
            {
                present = context.Items.ContainsKey("diagrid.user");
                return context.Response.WriteAsync("ok");
            });
        using var client = host.GetTestClient();

        await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(present);
    }

    private static Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, string header = SomeToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/invoke", UriKind.Relative));
        request.Headers.Add(IdentityContext.UserTokenHeader, header);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task WriteVerifiedUser(HttpContext context)
    {
        var user = context.GetVerifiedUser()!;
        return context.Response.WriteAsJsonAsync(
            new UserView(user.Subject, user.Tenant, [.. user.Scopes], user.IssuerId),
            context.RequestAborted);
    }

    private static async Task<UserView> ReadUserAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<UserView>(TestContext.Current.CancellationToken))!;

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetString()!;
    }

    private sealed record UserView(string Subject, string Tenant, string[] Scopes, string IssuerId);
}
