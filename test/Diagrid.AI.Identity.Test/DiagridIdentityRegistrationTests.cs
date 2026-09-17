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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Identity.Test;

public sealed class DiagridIdentityRegistrationTests : IDisposable
{
    private readonly TestSigningKey _key = new();

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task ScopedTokenVerifier_ServesRequests()
    {
        // A scoped verifier is the obvious shape for one that reads per-request state, and a
        // singleton middleware would pull it out of the root provider and fail on the first
        // request.
        using var host = await IdentityTestServer.StartAsync(
            configureServices: services => services.AddScoped<ITokenVerifier>(
                _ => FakeTokenVerifier.Accepting()));
        using var client = host.GetTestClient();

        var response = await SendWithTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Middleware_IsRegisteredScoped()
    {
        // An IMiddleware is resolved per request from RequestServices, so the registration
        // has to be scoped for a scoped dependency to be resolvable at all.
        var services = new ServiceCollection().AddDiagridIdentity();

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(OAuthMiddleware));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void VerifierProvider_IsRegisteredSingleton()
    {
        // The fallback verifier caches key material, so it outlives the request that built it.
        var services = new ServiceCollection().AddDiagridIdentity();

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(TokenVerifierProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public async Task FallbackVerifier_UsesTheNamedHttpClientAndIsBuiltOnce()
    {
        // Otherwise an app behind a proxy or a private CA has no way to influence how the
        // JWKS is fetched.
        var handler = StubHttpMessageHandler.Json(_key.ToJwksJson());
        using var host = await IdentityTestServer.StartAsync(
            config =>
            {
                config.Issuer = TokenFactory.Issuer;
                config.JwksUri = TokenFactory.JwksUri;
            },
            configureServices: services => services
                .AddHttpClient(DiagridIdentityServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler));
        using var client = host.GetTestClient();
        var token = TokenFactory.Create(_key);

        var first = await SendWithTokenAsync(client, "Bearer " + token);
        var second = await SendWithTokenAsync(client, "Bearer " + token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal([TokenFactory.JwksUri], handler.RequestedUris);
    }

    private static Task<HttpResponseMessage> SendWithTokenAsync(
        HttpClient client,
        string header = "Bearer fake.jwt.token")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/invoke", UriKind.Relative));
        request.Headers.Add(IdentityContext.UserTokenHeader, header);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
