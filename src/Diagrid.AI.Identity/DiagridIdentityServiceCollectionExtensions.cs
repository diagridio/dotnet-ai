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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Identity;

/// <summary>
/// DI extensions for registering inbound user-token verification.
/// </summary>
public static class DiagridIdentityServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> the fallback <see cref="JwksVerifier"/> uses
    /// for coordinate discovery and JWKS fetches.
    /// </summary>
    /// <remarks>
    /// Register a client under this name to send those requests through a proxy, a private
    /// CA, or any other custom handler:
    /// <code>
    /// builder.Services
    ///     .AddHttpClient(DiagridIdentityServiceCollectionExtensions.HttpClientName)
    ///     .ConfigurePrimaryHttpMessageHandler(() =&gt; new SocketsHttpHandler
    ///     {
    ///         PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    ///     });
    /// </code>
    /// Give any handler you configure a pooled-connection lifetime of its own: the verifier
    /// holds the client for the life of the application, which is long enough for a JWKS host
    /// to change address. The shared default used when no client is registered already
    /// recycles its connections.
    /// </remarks>
    public const string HttpClientName = "diagrid-identity";

    /// <summary>
    /// Registers the <see cref="OAuthMiddleware"/> and the policy it enforces.
    /// </summary>
    /// <remarks>
    /// Register an <see cref="ITokenVerifier"/> of your own before calling this to take over
    /// verification; otherwise the middleware builds a <see cref="JwksVerifier"/> from
    /// explicit configuration, sidecar metadata, or the <c>DIAGRID_DP_SENTRY_*</c>
    /// environment variables, in that order.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback configuring the <see cref="OAuthConfig"/>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDiagridIdentity(
        this IServiceCollection services,
        Action<OAuthConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<OAuthConfig>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // One verifier serves the whole application, so its key-set cache is a singleton.
        services.TryAddSingleton(serviceProvider => new TokenVerifierProvider(
            serviceProvider.GetRequiredService<IOptions<OAuthConfig>>(),
            serviceProvider.GetRequiredService<ILogger<TokenVerifierProvider>>(),
            serviceProvider.GetService<IHttpClientFactory>()));

        // The middleware is resolved per request from RequestServices, so it is registered
        // scoped: a singleton would pull ITokenVerifier out of the root provider and throw
        // on the first request for anyone who registered a scoped verifier of their own.
        services.TryAddScoped(serviceProvider => new OAuthMiddleware(
            serviceProvider.GetRequiredService<IOptions<OAuthConfig>>(),
            serviceProvider.GetService<ITokenVerifier>(),
            serviceProvider.GetRequiredService<TokenVerifierProvider>(),
            serviceProvider.GetRequiredService<ILogger<OAuthMiddleware>>()));

        return services;
    }
}
