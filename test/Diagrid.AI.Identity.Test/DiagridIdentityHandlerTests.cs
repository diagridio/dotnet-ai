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
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Identity.Test;

public sealed class DiagridIdentityHandlerTests : IDisposable
{
    private const string ClientName = DiagridIdentityHttpClientServiceCollectionExtensions.DefaultClientName;
    private const string OriginHost = "origin.test";
    private const string OriginUrl = "http://origin.test/call";
    private const string SameOriginUrl = "http://origin.test/next";
    private const string UpgradedUrl = "https://origin.test/call";
    private const string OtherOriginUrl = "https://elsewhere.test/hop";
    private const string StaleHeader = "Bearer stale-token";
    private const string SpoofHeader = "Bearer spoofed-token";
    private const string AppAuthorization = "Bearer app-access-token";
    private const string AppCookie = "session=app-session";
    private const string AppProxyAuthorization = "Basic cHJveHk6c2VjcmV0";
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    public void Dispose() => IdentityContext.ClearCurrentToken();

    [Fact]
    public async Task SendAsync_ReadsTheTokenAtSendTimeSoOneClientServesTwoCallers()
    {
        // The client is built before either caller exists, so a token captured at
        // construction time could only ever be nobody's.
        var arrived = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        var handler = new RecordingHttpMessageHandler(async _ =>
        {
            arrived.Release();
            await release.Task;
            return RecordingHttpMessageHandler.Ok();
        });
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        var alice = SendAsAsync(client, "alice-token");
        var bob = SendAsAsync(client, "bob-token");
        await arrived.WaitAsync(TestContext.Current.CancellationToken);
        await arrived.WaitAsync(TestContext.Current.CancellationToken);
        release.SetResult();
        await Task.WhenAll(alice, bob);

        Assert.Equal(
            ["Bearer alice-token", "Bearer bob-token"],
            handler.Requests.Select(request => request.IdentityHeader).Order());
    }

    [Fact]
    public async Task SendAsync_ClearsAStaleHeaderWhenThereIsNoCaller()
    {
        // Whatever set the header before, the request must never carry an identity the
        // current context does not hold.
        var handler = new RecordingHttpMessageHandler();
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAsync(client, staleHeader: StaleHeader);

        Assert.Null(Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_ReplacesAStaleHeaderWithTheCurrentCaller()
    {
        var handler = new RecordingHttpMessageHandler();
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("real-token");

        using var response = await SendAsync(client, staleHeader: StaleHeader);

        Assert.Equal("Bearer real-token", Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_OmitsTheHeaderEntirelyWithoutInboundContext()
    {
        // Scheduled, pub/sub and cron triggers have no caller: the call proceeds
        // unauthenticated with the header absent, not present and empty, and does not throw.
        var handler = new RecordingHttpMessageHandler();
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_CarriesTheCallerOnAPlainRequest()
    {
        var handler = new RecordingHttpMessageHandler();
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer tok", Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_LeavesTheCallersRequestUnmutated()
    {
        var handler = new RecordingHttpMessageHandler();
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(OriginUrl));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer tok", Assert.Single(handler.Requests).IdentityHeader);
        Assert.False(request.Headers.Contains(IdentityContext.UserTokenHeader));
    }

    [Fact]
    public async Task SendAsync_DropsTheHeaderOnARedirectToAnotherOrigin()
    {
        // Without this, a redirect from the callee hands the caller's on-behalf-of token to
        // whatever host the redirect names.
        var handler = RedirectingHandler(OtherOriginUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer tok", handler.Requests[0].IdentityHeader);
        Assert.Equal(new Uri(OtherOriginUrl), handler.Requests[1].Uri);
        Assert.Null(handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_StripsTheAppsOwnCredentialsOnARedirectToAnotherOrigin()
    {
        // Taking the redirect walk off HttpClient took its stripping of these three with it,
        // so the handler has to do it: every hop is rebuilt from the caller's own headers.
        var handler = RedirectingHandler(OtherOriginUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAsync(client, credentialed: true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(AppAuthorization, handler.Requests[0].Header("Authorization"));
        Assert.Equal(AppCookie, handler.Requests[0].Header("Cookie"));
        Assert.Equal(AppProxyAuthorization, handler.Requests[0].Header("Proxy-Authorization"));
        Assert.Null(handler.Requests[1].Header("Authorization"));
        Assert.Null(handler.Requests[1].Header("Cookie"));
        Assert.Null(handler.Requests[1].Header("Proxy-Authorization"));
    }

    [Fact]
    public async Task SendAsync_KeepsTheAppsOwnCredentialsOnASameOriginRedirect()
    {
        // A same-origin hop is still the host the caller addressed.
        var handler = RedirectingHandler(SameOriginUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAsync(client, credentialed: true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(AppAuthorization, handler.Requests[1].Header("Authorization"));
        Assert.Equal(AppCookie, handler.Requests[1].Header("Cookie"));
        Assert.Equal(AppProxyAuthorization, handler.Requests[1].Header("Proxy-Authorization"));
    }

    [Fact]
    public async Task SendAsync_KeepsUnrelatedHeadersOnARedirectToAnotherOrigin()
    {
        var handler = RedirectingHandler(OtherOriginUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAsync(client, credentialed: true);

        Assert.Equal(TraceParent, handler.Requests[1].Header("traceparent"));
    }

    [Fact]
    public async Task SendAsync_KeepsTheHeaderOnASameOriginRedirect()
    {
        var handler = RedirectingHandler(SameOriginUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(SameOriginUrl), handler.Requests[1].Uri);
        Assert.Equal("Bearer tok", handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_KeepsTheHeaderOnAnHttpToHttpsUpgrade()
    {
        // The one allowed exception: a same-host upgrade from http on port 80 to https on
        // port 443.
        var handler = RedirectingHandler(UpgradedUrl);
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(UpgradedUrl), handler.Requests[1].Uri);
        Assert.Equal("Bearer tok", handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_DropsTheHeaderOnAnHttpsToHttpDowngrade()
    {
        // The upgrade exception runs one way only: the same host over plaintext is not the
        // origin the caller addressed.
        var handler = new RecordingHttpMessageHandler(request => Task.FromResult(
            request.RequestUri!.Scheme == Uri.UriSchemeHttps
                ? RecordingHttpMessageHandler.Redirect(OriginUrl)
                : RecordingHttpMessageHandler.Ok()));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client, url: UpgradedUrl);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer tok", handler.Requests[0].IdentityHeader);
        Assert.Null(handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_FollowsARelativeRedirectAgainstTheCurrentHop()
    {
        var handler = new RecordingHttpMessageHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/call"
                ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/next", UriKind.Relative) } }
                : RecordingHttpMessageHandler.Ok()));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await SendAsync(client);

        Assert.Equal(new Uri(SameOriginUrl), handler.Requests[1].Uri);
        Assert.Equal("Bearer tok", handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task SendAsync_RewritesAPostToAGetOnASeeOtherRedirect()
    {
        var handler = new RecordingHttpMessageHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/call"
                ? RecordingHttpMessageHandler.Redirect(SameOriginUrl, HttpStatusCode.SeeOther)
                : RecordingHttpMessageHandler.Ok()));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(OriginUrl))
        {
            Content = new StringContent("body"),
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenTheRedirectBudgetIsExhausted()
    {
        // A spent budget is an error, not a 3xx handed back: a caller that got the redirect
        // response would read it as a completed exchange.
        var handler = new RecordingHttpMessageHandler(
            _ => Task.FromResult(RecordingHttpMessageHandler.Redirect(OriginUrl)));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(client));

        Assert.Contains("maximum number of redirects", exception.Message, StringComparison.Ordinal);
        Assert.Equal(DiagridIdentityHandler.MaxAutomaticRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task Handler_CanBeInstalledOnAClientTheAppAlreadyOwns()
    {
        var handler = RedirectingHandler(OtherOriginUrl);
        using var identityHandler = new DiagridIdentityHandler { InnerHandler = handler };
        using var client = new HttpClient(identityHandler);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await client.GetAsync(new Uri(OriginUrl), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer tok", handler.Requests[0].IdentityHeader);
        Assert.Null(handler.Requests[1].IdentityHeader);
    }

    [Fact]
    public async Task Handler_RunsAfterAppHandlersSoIdentityWins()
    {
        // The handler nearest the wire is the one whose header reaches the callee.
        var handler = new RecordingHttpMessageHandler();
        var appHandlerRan = false;
        using var provider = BuildProvider(
            handler,
            builder => builder.AddHttpMessageHandler(
                () => new SpoofingHandler(() => appHandlerRan = true)));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("real-token");

        using var response = await SendAsync(client);

        Assert.True(appHandlerRan);
        Assert.Equal("Bearer real-token", Assert.Single(handler.Requests).IdentityHeader);
    }

    private static RecordingHttpMessageHandler RedirectingHandler(string location) =>
        new(request => Task.FromResult(
            string.Equals(request.RequestUri!.Host, OriginHost, StringComparison.Ordinal)
            && request.RequestUri.AbsolutePath == "/call"
            && request.RequestUri.Scheme == Uri.UriSchemeHttp
                ? RecordingHttpMessageHandler.Redirect(location)
                : RecordingHttpMessageHandler.Ok()));

    private static ServiceProvider BuildProvider(
        RecordingHttpMessageHandler handler,
        Action<IHttpClientBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddDiagridIdentityHttpClient();
        configureBuilder?.Invoke(builder);
        builder.ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string? staleHeader = null,
        string url = OriginUrl,
        bool credentialed = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        if (staleHeader is not null)
        {
            request.Headers.TryAddWithoutValidation(IdentityContext.UserTokenHeader, staleHeader);
        }

        if (credentialed)
        {
            request.Headers.TryAddWithoutValidation("Authorization", AppAuthorization);
            request.Headers.TryAddWithoutValidation("Cookie", AppCookie);
            request.Headers.TryAddWithoutValidation("Proxy-Authorization", AppProxyAuthorization);
            request.Headers.TryAddWithoutValidation("traceparent", TraceParent);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task SendAsAsync(HttpClient client, string token) => Task.Run(
        async () =>
        {
            using var scope = IdentityContext.SetCurrentToken(token);
            using var response = await SendAsync(client);
        },
        TestContext.Current.CancellationToken);

    /// <summary>
    /// An app handler that sets the identity header itself, the way a hand-rolled
    /// propagation handler would.
    /// </summary>
    private sealed class SpoofingHandler(Action onSend) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            onSend();
            request.Headers.Remove(IdentityContext.UserTokenHeader);
            request.Headers.TryAddWithoutValidation(IdentityContext.UserTokenHeader, SpoofHeader);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
