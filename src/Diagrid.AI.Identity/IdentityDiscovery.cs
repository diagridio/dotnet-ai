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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Identity;

/// <summary>
/// Finds the identity coordinates from a local sidecar, a remote sidecar, or the environment.
/// </summary>
internal static class IdentityDiscovery
{
    internal const string CatalystDaprHttpPortVariable = "CATALYST_DAPR_HTTP_PORT";
    internal const string DaprHttpPortVariable = "DAPR_HTTP_PORT";
    internal const string DaprHttpEndpointVariable = "DAPR_HTTP_ENDPOINT";
    internal const string DaprApiTokenVariable = "DAPR_API_TOKEN";
    internal const string IssuerVariable = "DIAGRID_DP_SENTRY_ISSUER";
    internal const string AudienceVariable = "DIAGRID_DP_SENTRY_AUDIENCE";

    /// <summary>
    /// The metadata path both sidecar sources compose onto their own origin.
    /// </summary>
    internal const string MetadataPath = "/v1.0/metadata";

    internal const string LocalMetadataOriginFormat = "http://127.0.0.1:{0}";
    internal const string JwksPathSuffix = "/jwks.json";

    /// <summary>
    /// The header a Dapr sidecar authenticates an API call by.
    /// </summary>
    internal const string ApiTokenHeader = "dapr-api-token";

    internal static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(5);

    private const string IdentityProperty = "identity";
    private const string IssuerProperty = "issuer";
    private const string JwksUriProperty = "jwks_uri";
    private const string AudienceProperty = "audience";

    /// <summary>
    /// Tries <c>GET http://127.0.0.1:$PORT/v1.0/metadata</c> for the identity block.
    /// </summary>
    /// <param name="httpClient">The client used for the request.</param>
    /// <param name="logger">Receives a warning for a failed probe.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The discovered coordinates, or <see langword="null"/> when unavailable.</returns>
    internal static async Task<IdentityCoordinates?> DiscoverFromMetadataAsync(
        HttpClient httpClient,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var port = Environment.GetEnvironmentVariable(CatalystDaprHttpPortVariable);
        if (string.IsNullOrEmpty(port))
        {
            port = Environment.GetEnvironmentVariable(DaprHttpPortVariable);
        }

        if (string.IsNullOrEmpty(port))
        {
            return null;
        }

        var origin = string.Format(CultureInfo.InvariantCulture, LocalMetadataOriginFormat, port);

        return await ProbeMetadataAsync(httpClient, origin, apiToken: null, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tries <c>GET $DAPR_HTTP_ENDPOINT/v1.0/metadata</c> for the identity block.
    /// </summary>
    /// <param name="httpClient">The client used for the request.</param>
    /// <param name="logger">
    /// Receives a warning for a failed probe, and one for an API token sent in clear text.
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The discovered coordinates, or <see langword="null"/> when unavailable.</returns>
    /// <remarks>
    /// <c>diagrid dev run</c> runs the app on a developer's machine against a Catalyst-hosted
    /// sidecar, so 127.0.0.1 has nothing listening and the local probe cannot answer.
    /// </remarks>
    internal static async Task<IdentityCoordinates?> DiscoverFromRemoteAsync(
        HttpClient httpClient,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = (Environment.GetEnvironmentVariable(DaprHttpEndpointVariable) ?? string.Empty)
            .TrimEnd('/');

        // An unset endpoint is an absent source rather than a failed one, so nothing is logged.
        if (string.IsNullOrEmpty(endpoint))
        {
            return null;
        }

        var apiToken = Environment.GetEnvironmentVariable(DaprApiTokenVariable);
        if (!string.IsNullOrEmpty(apiToken) && !IsHttps(endpoint))
        {
            // Warned, but still sent: a self-hosted sidecar on plain http is a valid setup.
            (logger ?? NullLogger.Instance).LogWarning(
                "{TokenVariable} will be sent in clear text to non-https endpoint {Endpoint}",
                DaprApiTokenVariable,
                endpoint);
        }

        return await ProbeMetadataAsync(httpClient, endpoint, apiToken, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Falls back to the <c>DIAGRID_DP_SENTRY_*</c> environment variables.
    /// </summary>
    /// <returns>The discovered coordinates, or <see langword="null"/> when the issuer is unset.</returns>
    internal static IdentityCoordinates? DiscoverFromEnvironment()
    {
        var issuer = Environment.GetEnvironmentVariable(IssuerVariable) ?? string.Empty;
        if (string.IsNullOrEmpty(issuer))
        {
            return null;
        }

        return new IdentityCoordinates(
            issuer,
            DefaultJwksUri(issuer),
            Environment.GetEnvironmentVariable(AudienceVariable) ?? string.Empty);
    }

    /// <summary>
    /// Returns the JWKS endpoint an issuer serves by convention.
    /// </summary>
    /// <param name="issuer">The issuer URL.</param>
    /// <returns>The issuer with a single trailing slash removed and <c>/jwks.json</c> appended.</returns>
    internal static string DefaultJwksUri(string issuer) => issuer.TrimEnd('/') + JwksPathSuffix;

    private static bool IsHttps(string endpoint) =>
        endpoint.StartsWith(
            Uri.UriSchemeHttps + Uri.SchemeDelimiter,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Issues one metadata probe against <paramref name="origin"/> and reads the identity
    /// block out of the response.
    /// </summary>
    /// <remarks>
    /// The body is parsed inside the same <c>try</c>, so a malformed response is no discovery
    /// exactly as an unreachable one is and the caller moves on to the next source.
    /// </remarks>
    private static async Task<IdentityCoordinates?> ProbeMetadataAsync(
        HttpClient httpClient,
        string origin,
        string? apiToken,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var url = origin + MetadataPath;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MetadataTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            if (!string.IsNullOrEmpty(apiToken))
            {
                request.Headers.TryAddWithoutValidation(ApiTokenHeader, apiToken);
            }

            using var response = await httpClient
                .SendAsync(request, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);

            return CoordinatesFromIdentity(body);
        }
        catch (Exception ex) when (ex is HttpRequestException
            or OperationCanceledException
            or InvalidOperationException
            or JsonException
            or FormatException)
        {
            // Names the failure, never the API token, which would then sit in the log in
            // clear text whatever the transport was.
            (logger ?? NullLogger.Instance).LogWarning(
                "identity discovery via {Url} failed ({ExceptionType}: {ExceptionMessage}); trying the next source",
                url,
                ex.GetType().Name,
                ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads the identity block out of a <c>/v1.0/metadata</c> response body.
    /// </summary>
    /// <remarks>
    /// Called inside the caller's <c>try</c>: a malformed body throws and is treated as no
    /// discovery.
    /// </remarks>
    private static IdentityCoordinates? CoordinatesFromIdentity(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(IdentityProperty, out var identity)
            || identity.ValueKind != JsonValueKind.Object
            || !identity.TryGetProperty(IssuerProperty, out var issuerElement))
        {
            return null;
        }

        var issuer = issuerElement.GetString();
        if (string.IsNullOrEmpty(issuer))
        {
            return null;
        }

        var jwksUri = identity.TryGetProperty(JwksUriProperty, out var jwksElement)
            ? jwksElement.GetString()
            : null;
        var audience = identity.TryGetProperty(AudienceProperty, out var audienceElement)
            ? audienceElement.GetString()
            : null;

        return new IdentityCoordinates(
            issuer,
            string.IsNullOrEmpty(jwksUri) ? DefaultJwksUri(issuer) : jwksUri,
            audience ?? string.Empty);
    }
}
