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

using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Identity;

/// <summary>
/// Installs the <see cref="DiagridIdentityHandler"/> on the clients that opted in.
/// </summary>
/// <remarks>
/// A filter rather than a plain <c>AddHttpMessageHandler</c> call because both properties it
/// holds depend on running after every registration the app made: the identity handler
/// belongs below any handler the app added, since the handler nearest the wire is the one
/// whose header the callee sees, and automatic redirects have to be off on whichever primary
/// handler the app configured last.
/// </remarks>
internal sealed class DiagridIdentityHandlerFilter(
    IOptionsMonitor<DiagridIdentityHttpClientOptions> options,
    ILoggerFactory loggerFactory) : IHttpMessageHandlerBuilderFilter
{
    /// <inheritdoc />
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            ArgumentNullException.ThrowIfNull(builder);

            next(builder);

            var clientOptions = options.Get(builder.Name);
            if (!clientOptions.Enabled)
            {
                return;
            }

            var logger = loggerFactory.CreateLogger<DiagridIdentityHandler>();
            if (clientOptions.FollowRedirects)
            {
                DisableAutomaticRedirects(builder.PrimaryHandler, builder.Name, logger);
            }
            else
            {
                logger.LogWarning(
                    "client {Client} declined outbound identity redirect following; a redirect "
                    + "followed below the identity handler bypasses the origin guard, so the "
                    + "identity header can reach a host the caller did not address",
                    builder.Name);
            }

            builder.AdditionalHandlers.Add(
                new DiagridIdentityHandler(logger) { FollowRedirects = clientOptions.FollowRedirects });
        };
    }

    /// <summary>
    /// Stops the primary handler from following redirects on its own.
    /// </summary>
    /// <remarks>
    /// The redirect would otherwise be followed below the identity handler, which is never
    /// re-invoked for the hop, leaving the origin guard unable to fire.
    /// </remarks>
    /// <param name="primaryHandler">The handler at the bottom of the pipeline.</param>
    /// <param name="clientName">The client being built, for the diagnostic.</param>
    /// <param name="logger">Receives the diagnostic when the handler cannot be reconfigured.</param>
    private static void DisableAutomaticRedirects(
        HttpMessageHandler primaryHandler,
        string? clientName,
        ILogger logger)
    {
        switch (primaryHandler)
        {
            case HttpClientHandler httpClientHandler:
                httpClientHandler.AllowAutoRedirect = false;
                break;
            case SocketsHttpHandler socketsHttpHandler:
                socketsHttpHandler.AllowAutoRedirect = false;
                break;
            default:
                logger.LogDebug(
                    "primary handler {Handler} on client {Client} has no AllowAutoRedirect to "
                    + "turn off; a redirect it follows itself bypasses the outbound identity "
                    + "origin guard",
                    primaryHandler.GetType().Name,
                    clientName);
                break;
        }
    }
}
