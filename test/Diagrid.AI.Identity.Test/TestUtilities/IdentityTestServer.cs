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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// Spins up an in-memory app with the middleware installed the way a consumer installs it.
/// </summary>
internal static class IdentityTestServer
{
    /// <summary>
    /// Starts a host whose pipeline is <c>UseDiagridIdentity()</c> followed by
    /// <paramref name="handler"/>.
    /// </summary>
    /// <param name="configure">Configures the policy, exactly as an app would.</param>
    /// <param name="verifier">
    /// The verifier to register, or <see langword="null"/> to let the middleware discover
    /// its own coordinates.
    /// </param>
    /// <param name="handler">The terminal endpoint.</param>
    /// <param name="onRequestCompleted">
    /// Invoked outside the middleware once the request has unwound, so a test can observe
    /// what the middleware restored.
    /// </param>
    /// <param name="configureServices">
    /// Registers further services before <c>AddDiagridIdentity</c>, the way an app would.
    /// </param>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>The started host; call <c>GetTestClient()</c> on it.</returns>
    internal static Task<IHost> StartAsync(
        Action<OAuthConfig>? configure = null,
        ITokenVerifier? verifier = null,
        RequestDelegate? handler = null,
        Action? onRequestCompleted = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default) =>
        new HostBuilder()
            .ConfigureWebHost(webBuilder => webBuilder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    if (verifier is not null)
                    {
                        services.AddSingleton(verifier);
                    }

                    configureServices?.Invoke(services);
                    services.AddDiagridIdentity(configure);
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        await next(context);
                        onRequestCompleted?.Invoke();
                    });

                    app.UseDiagridIdentity();
                    app.Run(handler ?? (context => context.Response.WriteAsync("ok")));
                }))
            .StartAsync(cancellationToken);
}
