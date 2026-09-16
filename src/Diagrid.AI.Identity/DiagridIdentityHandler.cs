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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Identity;

/// <summary>
/// Carries the calling user's identity on outbound on-behalf-of calls.
/// </summary>
/// <remarks>
/// <para>
/// Reach for
/// <see cref="DiagridIdentityHttpClientServiceCollectionExtensions.AddDiagridIdentityHttpClient"/>
/// first: it registers a named <see cref="HttpClient"/> with this handler installed and the
/// primary handler set up the way the origin guard needs. Install the handler yourself only
/// when you already own a client you cannot replace:
/// <code>
/// using var client = new HttpClient(new DiagridIdentityHandler
/// {
///     InnerHandler = new HttpClientHandler { AllowAutoRedirect = false },
/// });
/// </code>
/// </para>
/// <para>
/// The token is read from <see cref="IdentityContext"/> at send time rather than captured
/// when the client is built. That is what makes one long-lived, shared client safe:
/// concurrent requests each carry their own caller's token, where a token baked in at
/// construction would send whichever user happened to be current when the client was made.
/// A request made with no inbound context carries no identity header at all rather than an
/// empty one.
/// </para>
/// <para>
/// The token goes only to the origin the caller addressed. To hold that line across a
/// redirect the handler follows redirect responses itself, which works only while the
/// handler below it does not: with <see cref="HttpClientHandler.AllowAutoRedirect"/> left
/// on, the hop is followed underneath this handler, <c>SendAsync</c> is never re-invoked
/// for it, and the origin guard cannot fire. The DI registration turns it off for you;
/// installing the handler by hand means turning it off yourself, as above. A hop this
/// handler follows is sent from here, so handlers registered above it do not see it, and a
/// 307 or 308 hop resends the original body — which a one-shot stream body cannot do.
/// </para>
/// </remarks>
public sealed class DiagridIdentityHandler : DelegatingHandler
{
    /// <summary>
    /// How many redirects the handler follows before it gives up, matching the default of
    /// <see cref="HttpClientHandler.MaxAutomaticRedirections"/>.
    /// </summary>
    /// <remarks>
    /// Exhausting the budget throws <see cref="HttpRequestException"/>; handing the redirect
    /// response back instead would read to a caller as a completed exchange.
    /// </remarks>
    internal const int MaxAutomaticRedirects = 50;

    private const int HttpDefaultPort = 80;
    private const int HttpsDefaultPort = 443;

    /// <summary>
    /// The application's own credential headers, dropped from a hop that leaves the origin
    /// the caller addressed.
    /// </summary>
    /// <remarks>
    /// The identity header has its own origin rule; these are the credentials the application
    /// set itself.
    /// </remarks>
    private static readonly string[] CrossOriginStrippedHeaders =
    [
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
    ];

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagridIdentityHandler"/> class that
    /// logs nowhere.
    /// </summary>
    public DiagridIdentityHandler()
        : this(NullLogger<DiagridIdentityHandler>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagridIdentityHandler"/> class.
    /// </summary>
    /// <param name="logger">
    /// Receives, at debug level, the calls that went out without an identity: a request made
    /// with no inbound context, and a redirect hop that left the origin.
    /// </param>
    public DiagridIdentityHandler(ILogger<DiagridIdentityHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the handler follows redirect responses itself.
    /// </summary>
    /// <remarks>
    /// On by default, and what the origin guard depends on: a hop followed below this handler
    /// never re-enters <c>SendAsync</c>, so the guard cannot fire for it. Set it to
    /// <see langword="false"/> to hand the redirect response straight back and leave redirect
    /// policy to the handler below — the identity header then rides whatever hops that
    /// handler follows.
    /// </remarks>
    public bool FollowRedirects { get; init; } = true;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // HttpClient resolves BaseAddress into RequestUri before the pipeline runs, so a null
        // here fails below us anyway — and there is no origin to pin an identity to.
        var origin = request.RequestUri;
        if (origin is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var target = origin;
        var method = request.Method;
        var content = request.Content;

        for (var hop = 0; ; hop++)
        {
            var hopRequest = CloneWithIdentity(request, method, target, content, origin);
            var response = await base.SendAsync(hopRequest, cancellationToken).ConfigureAwait(false);

            var location = FollowRedirects ? RedirectTarget(response, target) : null;
            if (location is null)
            {
                return response;
            }

            if (hop >= MaxAutomaticRedirects)
            {
                response.Dispose();
                throw new HttpRequestException(
                    $"The maximum number of redirects ({MaxAutomaticRedirects}) has been exceeded.");
            }

            (method, content) = RedirectMethod(response.StatusCode, method, content);
            target = location;
            response.Dispose();
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> is still the origin the caller addressed.
    /// </summary>
    /// <remarks>
    /// The one exception is a same-host upgrade from HTTP on port 80 to HTTPS on port 443.
    /// <see cref="HttpClient"/> would strip <c>Authorization</c> itself on a hop that leaves
    /// the origin, but it is not the one following the hop here, so this handler answers for
    /// the caller's on-behalf-of token and for <see cref="CrossOriginStrippedHeaders"/> both.
    /// </remarks>
    /// <param name="origin">The URI the caller asked for.</param>
    /// <param name="target">The URI about to be requested.</param>
    /// <returns><see langword="true"/> when the identity may ride along.</returns>
    private static bool IsOriginalOrigin(Uri origin, Uri target) =>
        Uri.Compare(
            origin,
            target,
            UriComponents.SchemeAndServer,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0
        || (string.Equals(origin.Host, target.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Scheme == Uri.UriSchemeHttp
            && origin.Port == HttpDefaultPort
            && target.Scheme == Uri.UriSchemeHttps
            && target.Port == HttpsDefaultPort);

    private static Uri? RedirectTarget(HttpResponseMessage response, Uri current)
    {
        if (!IsRedirect(response.StatusCode))
        {
            return null;
        }

        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        return location.IsAbsoluteUri ? location : new Uri(current, location);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect => true,
        _ => false,
    };

    /// <summary>
    /// The method and body the next hop carries.
    /// </summary>
    /// <remarks>
    /// 301 and 302 rewrite a POST to a GET and 303 rewrites anything but GET and HEAD, as
    /// <see cref="HttpClient"/> does when it follows a redirect itself; 307 and 308 preserve
    /// both.
    /// </remarks>
    /// <param name="statusCode">The redirect status.</param>
    /// <param name="method">The method of the hop just answered.</param>
    /// <param name="content">The body of the hop just answered.</param>
    /// <returns>The method and body for the next hop.</returns>
    private static (HttpMethod Method, HttpContent? Content) RedirectMethod(
        HttpStatusCode statusCode,
        HttpMethod method,
        HttpContent? content)
    {
        var forceGet = statusCode switch
        {
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found => method == HttpMethod.Post,
            HttpStatusCode.SeeOther => method != HttpMethod.Get && method != HttpMethod.Head,
            _ => false,
        };

        return forceGet ? (HttpMethod.Get, null) : (method, content);
    }

    /// <summary>
    /// Copies <paramref name="source"/> and stamps the current caller's identity on the copy.
    /// </summary>
    /// <remarks>
    /// A copy because the request belongs to the caller, who may reuse or inspect it, and
    /// because every redirect hop starts from the headers the caller set rather than from the
    /// identity the hop before it carried — which is also why
    /// <see cref="CrossOriginStrippedHeaders"/> has to be dropped here on every hop.
    /// </remarks>
    /// <param name="source">The request the caller built.</param>
    /// <param name="method">The method for this hop.</param>
    /// <param name="target">The URI for this hop.</param>
    /// <param name="content">The body for this hop.</param>
    /// <param name="origin">The URI the caller addressed.</param>
    /// <returns>The request to send.</returns>
    private HttpRequestMessage CloneWithIdentity(
        HttpRequestMessage source,
        HttpMethod method,
        Uri target,
        HttpContent? content,
        Uri origin)
    {
        var sameOrigin = IsOriginalOrigin(origin, target);

        var clone = new HttpRequestMessage(method, target)
        {
            Content = content,
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        foreach (var header in source.Headers)
        {
            if (!sameOrigin && CrossOriginStrippedHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)source.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        // Whatever set it — the caller, an outer handler, a previous hop — the request must
        // not carry an identity this context does not hold.
        clone.Headers.Remove(IdentityContext.UserTokenHeader);

        if (!sameOrigin)
        {
            _logger.LogDebug(
                "identity and credential headers withheld: {Url} is not the origin called",
                target);
            return clone;
        }

        var headers = IdentityContext.OutboundIdentityHeaders();
        if (headers.Count == 0)
        {
            // Scheduled, pub/sub and cron triggers have no caller: not an error, the call
            // goes out unauthenticated.
            _logger.LogDebug("no inbound user context; calling {Url} unauthenticated", target);
            return clone;
        }

        foreach (var (name, value) in headers)
        {
            clone.Headers.TryAddWithoutValidation(name, value);
        }

        return clone;
    }
}
