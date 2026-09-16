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
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// Serves canned responses so JWKS fetches and metadata probes never leave the process.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
    private volatile string _jsonBody = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class that
    /// answers every request through <paramref name="responder"/>.
    /// </summary>
    /// <param name="responder">Builds the response for a request.</param>
    internal StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    private StubHttpMessageHandler(string jsonBody) => _jsonBody = jsonBody;

    /// <summary>
    /// Gets the absolute URIs the handler was asked for, in order.
    /// </summary>
    internal List<string> RequestedUris { get; } = [];

    /// <summary>
    /// Gets or sets the body served by a handler built with <see cref="Json(string)"/>, so a
    /// test can rotate the document a live client re-fetches.
    /// </summary>
    internal string JsonBody
    {
        get => _jsonBody;
        set => _jsonBody = value;
    }

    /// <summary>
    /// Builds a handler that answers every request with the same JSON body.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <returns>The handler.</returns>
    internal static StubHttpMessageHandler Json(string json) => new(json);

    /// <summary>
    /// Builds a handler that answers every request with the same status and no body.
    /// </summary>
    /// <param name="statusCode">The status to return.</param>
    /// <returns>The handler.</returns>
    internal static StubHttpMessageHandler Status(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));

    /// <summary>
    /// Builds a JSON response message.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <returns>The response.</returns>
    internal static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8)
            {
                Headers = { ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json) },
            },
        };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestedUris.Add(request.RequestUri!.ToString());
        return Task.FromResult(
            _responder is not null ? _responder(request) : JsonResponse(_jsonBody));
    }
}
