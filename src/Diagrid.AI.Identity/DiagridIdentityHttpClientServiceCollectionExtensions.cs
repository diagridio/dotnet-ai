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
using Microsoft.Extensions.Http;

namespace Diagrid.AI.Identity;

/// <summary>
/// DI extensions for registering an identity-aware <see cref="HttpClient"/>.
/// </summary>
public static class DiagridIdentityHttpClientServiceCollectionExtensions
{
    /// <summary>
    /// The name <see cref="AddDiagridIdentityHttpClient"/> registers the client under when
    /// none is given.
    /// </summary>
    public const string DefaultClientName = "diagrid-identity-outbound";

    /// <summary>
    /// Registers an <see cref="HttpClient"/> that carries the calling user's identity on
    /// every outbound request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The application never assembles an identity header itself:
    /// <code>
    /// builder.Services.AddDiagridIdentityHttpClient();
    ///
    /// app.MapGet("/ask", async (IHttpClientFactory factory) =&gt;
    /// {
    ///     var client = factory.CreateClient(
    ///         DiagridIdentityHttpClientServiceCollectionExtensions.DefaultClientName);
    ///
    ///     return await client.GetStringAsync(McpUrl);
    /// });
    /// </code>
    /// </para>
    /// <para>
    /// What comes back is a plain <see cref="HttpClient"/> from
    /// <see cref="IHttpClientFactory"/>, not a subclass, so it goes anywhere an
    /// <see cref="HttpClient"/> goes. Handlers added through the returned
    /// <see cref="IHttpClientBuilder"/> are kept, and identity is applied below them, so it
    /// wins over anything that set the same header.
    /// </para>
    /// <para>
    /// The registration also turns <see cref="HttpClientHandler.AllowAutoRedirect"/> off,
    /// because <see cref="DiagridIdentityHandler"/> follows redirects itself to keep the
    /// token from riding a hop that leaves the origin the caller addressed. Configure the
    /// primary handler freely; just leave that one property alone.
    /// </para>
    /// <para>
    /// Every call this client makes carries the caller's token, so call third-party APIs with
    /// a plain <see cref="HttpClient"/> instead — and note that <c>X-Diagrid-User-Token</c> is
    /// not a header name log scrubbers and tracing SDKs redact by default.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">
    /// The client name to register, or <see langword="null"/> for
    /// <see cref="DefaultClientName"/>.
    /// </param>
    /// <param name="configure">
    /// Optional callback configuring the <see cref="HttpClient"/>, exactly as the
    /// <c>AddHttpClient</c> overload of the same shape.
    /// </param>
    /// <param name="followRedirects">
    /// Whether <see cref="DiagridIdentityHandler"/> follows redirects itself, which is what
    /// lets it withhold the identity from a hop that leaves the origin. On by default. Pass
    /// <see langword="false"/> to leave redirect policy to the primary handler — the identity
    /// header then rides whatever hops that handler follows, which is logged as a warning at
    /// registration.
    /// </param>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> for the registered client, for chaining.
    /// </returns>
    public static IHttpClientBuilder AddDiagridIdentityHttpClient(
        this IServiceCollection services,
        string? name = null,
        Action<HttpClient>? configure = null,
        bool followRedirects = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        var clientName = name ?? DefaultClientName;

        services.Configure<DiagridIdentityHttpClientOptions>(
            clientName,
            options =>
            {
                options.Enabled = true;
                options.FollowRedirects = followRedirects;
            });

        // One filter serves every client that opts in, so a second registration must not
        // add a second identity handler to the first client.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, DiagridIdentityHandlerFilter>());

        return configure is null
            ? services.AddHttpClient(clientName)
            : services.AddHttpClient(clientName, configure);
    }
}
