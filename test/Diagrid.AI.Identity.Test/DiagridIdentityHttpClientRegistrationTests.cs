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
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Identity.Test;

public sealed class DiagridIdentityHttpClientRegistrationTests : IDisposable
{
    private const string ClientName = DiagridIdentityHttpClientServiceCollectionExtensions.DefaultClientName;
    private const string OtherClientName = "unrelated";
    private const string BaseAddress = "http://origin.test/";

    public void Dispose() => IdentityContext.ClearCurrentToken();

    [Fact]
    public void AddDiagridIdentityHttpClient_HandsBackAPlainHttpClient()
    {
        // Not a subclass, so the app can pass it anywhere an HttpClient is expected.
        var services = new ServiceCollection();
        services.AddDiagridIdentityHttpClient();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        Assert.IsType<HttpClient>(client);
    }

    [Fact]
    public async Task AddDiagridIdentityHttpClient_TakesANameAndTheUsualClientOptions()
    {
        var handler = new RecordingHttpMessageHandler();
        var services = new ServiceCollection();
        services
            .AddDiagridIdentityHttpClient(
                OtherClientName,
                client => client.BaseAddress = new Uri(BaseAddress))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(OtherClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await client.GetAsync(
            new Uri("call", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(new Uri(BaseAddress), client.BaseAddress);
        Assert.Equal("Bearer tok", Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public void Registration_TurnsOffAutomaticRedirectsOnThePrimaryHandler()
    {
        // With AllowAutoRedirect on, the hop is followed in the primary handler below the
        // identity handler, which is never re-invoked, so the origin guard cannot fire.
        var builder = new TestHttpMessageHandlerBuilder
        {
            Name = ClientName,
            PrimaryHandler = new HttpClientHandler { AllowAutoRedirect = true },
        };

        Filter().Configure(_ => { })(builder);

        Assert.False(Assert.IsType<HttpClientHandler>(builder.PrimaryHandler).AllowAutoRedirect);
    }

    [Fact]
    public void Registration_LeavesRedirectPolicyAloneWhenTheAppDeclinesRedirectFollowing()
    {
        // Still the app's call: an app that wants the platform's redirect behaviour can have
        // it, and is warned that the guard cannot fire on a hop the platform follows.
        var builder = new TestHttpMessageHandlerBuilder
        {
            Name = ClientName,
            PrimaryHandler = new HttpClientHandler { AllowAutoRedirect = true },
        };
        var logger = new RecordingLogger();

        Filter(followRedirects: false, logger).Configure(_ => { })(builder);

        Assert.True(Assert.IsType<HttpClientHandler>(builder.PrimaryHandler).AllowAutoRedirect);
        Assert.False(
            Assert.IsType<DiagridIdentityHandler>(Assert.Single(builder.AdditionalHandlers))
                .FollowRedirects);
        Assert.Contains(
            "origin guard",
            Assert.Single(logger.Warnings),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeclinedRedirectFollowing_HandsTheRedirectBackUntouched()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => Task.FromResult(RecordingHttpMessageHandler.Redirect(BaseAddress + "next")));
        var services = new ServiceCollection();
        services
            .AddDiagridIdentityHttpClient(followRedirects: false)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var scope = IdentityContext.SetCurrentToken("tok");

        using var response = await client.GetAsync(
            new Uri(BaseAddress + "call"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("Bearer tok", Assert.Single(handler.Requests).IdentityHeader);
    }

    [Fact]
    public void Registration_PutsTheIdentityHandlerLastSoItWinsOverAppHandlers()
    {
        var appHandler = new PassThroughHandler();
        var builder = new TestHttpMessageHandlerBuilder { Name = ClientName };
        builder.AdditionalHandlers.Add(appHandler);

        Filter().Configure(_ => { })(builder);

        Assert.Equal(2, builder.AdditionalHandlers.Count);
        Assert.Same(appHandler, builder.AdditionalHandlers[0]);
        Assert.IsType<DiagridIdentityHandler>(builder.AdditionalHandlers[1]);
    }

    [Fact]
    public void Registration_LeavesClientsThatDidNotOptInAlone()
    {
        var builder = new TestHttpMessageHandlerBuilder
        {
            Name = OtherClientName,
            PrimaryHandler = new HttpClientHandler { AllowAutoRedirect = true },
        };

        Filter().Configure(_ => { })(builder);

        Assert.Empty(builder.AdditionalHandlers);
        Assert.True(Assert.IsType<HttpClientHandler>(builder.PrimaryHandler).AllowAutoRedirect);
    }

    [Fact]
    public void Registration_InstallsOneFilterHoweverManyClientsOptIn()
    {
        var services = new ServiceCollection();
        services.AddDiagridIdentityHttpClient();
        services.AddDiagridIdentityHttpClient(OtherClientName);
        using var provider = services.BuildServiceProvider();

        var filters = provider.GetServices<IHttpMessageHandlerBuilderFilter>()
            .OfType<DiagridIdentityHandlerFilter>();

        Assert.Single(filters);
    }

    private static DiagridIdentityHandlerFilter Filter(
        bool followRedirects = true,
        RecordingLogger? logger = null)
    {
        var services = new ServiceCollection();
        services.AddDiagridIdentityHttpClient(followRedirects: followRedirects);
        if (logger is not null)
        {
            // Registered after AddHttpClient's own TryAdd, so this is the factory the filter
            // resolves.
            services.AddSingleton<ILoggerFactory>(new SingleLoggerFactory(logger));
        }

        var provider = services.BuildServiceProvider();

        return provider.GetServices<IHttpMessageHandlerBuilderFilter>()
            .OfType<DiagridIdentityHandlerFilter>()
            .Single();
    }

    private sealed class PassThroughHandler : DelegatingHandler;

    /// <summary>
    /// Hands out one logger whatever category is asked for.
    /// </summary>
    private sealed class SingleLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }
}
