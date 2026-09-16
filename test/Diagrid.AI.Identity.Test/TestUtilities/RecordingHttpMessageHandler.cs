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
using System.Text;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// A primary handler that records what actually reached the wire.
/// </summary>
/// <remarks>
/// Sits at the bottom of the outbound pipeline, below every delegating handler, so the
/// requests it records are the requests a callee would have received — including the
/// redirect hops the identity handler follows itself.
/// </remarks>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    internal const string OkBody = "ok";

    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="responder">
    /// Builds the response for a request, or <see langword="null"/> to answer every request
    /// with 200 <see cref="OkBody"/>.
    /// </param>
    internal RecordingHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? responder = null) =>
        _responder = responder ?? (_ => Task.FromResult(Ok()));

    /// <summary>
    /// Gets a snapshot of the requests the handler was asked for, in order.
    /// </summary>
    internal IReadOnlyList<RecordedRequest> Requests
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
    /// Builds a 200 response.
    /// </summary>
    /// <returns>The response.</returns>
    internal static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(OkBody, Encoding.UTF8) };

    /// <summary>
    /// Builds a redirect response pointing at <paramref name="location"/>.
    /// </summary>
    /// <param name="location">The <c>Location</c> header value.</param>
    /// <param name="statusCode">The redirect status to return.</param>
    /// <returns>The response.</returns>
    internal static HttpResponseMessage Redirect(
        string location,
        HttpStatusCode statusCode = HttpStatusCode.Found) =>
        new(statusCode) { Headers = { Location = new Uri(location) } };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = request.Headers.TryGetValues(IdentityContext.UserTokenHeader, out var values)
            ? string.Join(",", values)
            : null;

        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);

        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.RequestUri!, request.Method, identity, headers));
        }

        return _responder(request);
    }
}

/// <summary>
/// One request as it reached the bottom of the outbound pipeline.
/// </summary>
/// <param name="Uri">The absolute request URI.</param>
/// <param name="Method">The request method.</param>
/// <param name="IdentityHeader">
/// The <c>X-Diagrid-User-Token</c> value, or <see langword="null"/> when the header was
/// absent altogether.
/// </param>
/// <param name="Headers">
/// Every request header that reached the wire, keyed case-insensitively, values joined with
/// commas.
/// </param>
internal sealed record RecordedRequest(
    Uri Uri,
    HttpMethod Method,
    string? IdentityHeader,
    IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>
    /// Returns the value of one request header.
    /// </summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns>The value, or <see langword="null"/> when the header was absent.</returns>
    internal string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}
