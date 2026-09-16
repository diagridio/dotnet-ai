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
using System.Net.Mime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// A real HTTP server bound to 127.0.0.1 that answers <c>/v1.0/metadata</c> and
/// <c>/jwks.json</c>, and records what it was actually sent.
/// </summary>
/// <remarks>
/// A stub message handler can only show what the client believed it was sending, and what a
/// discovery test has to settle is what arrived at the other end. Loopback only: nothing
/// here leaves the machine.
/// </remarks>
internal sealed class LoopbackMetadataServer : IAsyncDisposable
{
    private const string NoIdentityMetadata = """{"id":"test-app"}""";
    private const string EmptyJwks = """{"keys":[]}""";

    private readonly List<ReceivedMetadataRequest> _requests = [];
    private IHost? _host;

    private LoopbackMetadataServer()
    {
    }

    /// <summary>
    /// Gets the scheme, host and port the server is listening on, with no trailing slash.
    /// </summary>
    internal string Origin { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the port the server is listening on.
    /// </summary>
    internal int Port { get; private set; }

    /// <summary>
    /// Gets the URL a discovery probe of this server has to request.
    /// </summary>
    internal string MetadataUrl => Origin + IdentityDiscovery.MetadataPath;

    /// <summary>
    /// Gets the URL this server publishes its key set at.
    /// </summary>
    internal string JwksUri => Origin + IdentityDiscovery.JwksPathSuffix;

    /// <summary>
    /// Gets or sets the body served at <c>/v1.0/metadata</c>.
    /// </summary>
    internal string MetadataJson { get; set; } = NoIdentityMetadata;

    /// <summary>
    /// Gets or sets the body served at <c>/jwks.json</c>.
    /// </summary>
    internal string JwksJson { get; set; } = EmptyJwks;

    /// <summary>
    /// Gets a snapshot of the requests the server received, in order.
    /// </summary>
    internal IReadOnlyList<ReceivedMetadataRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Starts a server on an arbitrary free loopback port.
    /// </summary>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>The started server.</returns>
    internal static async Task<LoopbackMetadataServer> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var server = new LoopbackMetadataServer();
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseKestrel(options => options.Listen(IPAddress.Loopback, 0))
                .Configure(app => app.Run(server.HandleAsync)))
            .Build();

        await host.StartAsync(cancellationToken);
        server._host = host;

        var address = host.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        server.Origin = address.TrimEnd('/');
        server.Port = new Uri(server.Origin).Port;
        return server;
    }

    /// <summary>
    /// Serves an identity block naming <paramref name="issuer"/>.
    /// </summary>
    /// <param name="issuer">The issuer to publish.</param>
    /// <param name="jwksUri">
    /// The JWKS endpoint to publish, or <see langword="null"/> to omit it so the consumer
    /// derives one.
    /// </param>
    internal void PublishIdentity(string issuer, string? jwksUri = null) =>
        MetadataJson = jwksUri is null
            ? $$$"""{"id":"test-app","identity":{"issuer":"{{{issuer}}}"}}"""
            : $$$"""{"id":"test-app","identity":{"issuer":"{{{issuer}}}","jwks_uri":"{{{jwksUri}}}"}}""";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var apiToken = context.Request.Headers.TryGetValue(IdentityDiscovery.ApiTokenHeader, out var values)
            ? values.ToString()
            : null;

        lock (_requests)
        {
            _requests.Add(new ReceivedMetadataRequest(path, apiToken));
        }

        var body = path switch
        {
            IdentityDiscovery.MetadataPath => MetadataJson,
            IdentityDiscovery.JwksPathSuffix => JwksJson,
            _ => null,
        };

        if (body is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(body);
    }
}

/// <summary>
/// One request as the loopback server received it.
/// </summary>
/// <param name="Path">The request path.</param>
/// <param name="ApiToken">
/// The <c>dapr-api-token</c> header value, or <see langword="null"/> when the header was
/// absent altogether.
/// </param>
internal sealed record ReceivedMetadataRequest(string Path, string? ApiToken);
