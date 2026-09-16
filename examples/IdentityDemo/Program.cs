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

using System.Text.Json.Serialization;
using Diagrid.AI.Identity;

const string DownstreamUrlVariable = "DOWNSTREAM_URL";
const string DefaultDownstreamUrl = "http://localhost:8081/whoami";
const string ReadScope = "read";
const string DownstreamUnreachable = "downstream_unreachable";

var downstreamUrl = Environment.GetEnvironmentVariable(DownstreamUrlVariable) ?? DefaultDownstreamUrl;

var builder = WebApplication.CreateBuilder(args);

// The default policy is fail-closed: RequireAuth is on, and the issuer, audience and JWKS
// URI are discovered from the sidecar's /v1.0/metadata.
builder.Services.AddDiagridIdentity();

// A named HttpClient that carries the calling user's identity on every request it makes, so
// the app never assembles an identity header itself.
builder.Services.AddDiagridIdentityHttpClient();

var app = builder.Build();

// Verifies X-Diagrid-User-Token ahead of every endpoint below.
app.UseDiagridIdentity();

// GetVerifiedUser is non-null here because RequireAuth rejects a tokenless request before
// it reaches the handler.
app.MapGet("/whoami", (HttpContext context) =>
{
    var user = context.GetVerifiedUser()!;

    return Results.Ok(new WhoAmIResponse(
        user.Subject,
        user.Tenant,
        [.. user.Scopes],
        user.HasScope(ReadScope)));
});

// An ordinary GET on the identity-aware client: it reads the inbound token at send time, so
// the callee verifies the same user this request was verified as.
app.MapGet("/downstream", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var client = httpClientFactory.CreateClient(
        DiagridIdentityHttpClientServiceCollectionExtensions.DefaultClientName);

    try
    {
        using var response = await client.GetAsync(new Uri(downstreamUrl), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return Results.Ok(new DownstreamResponse(body));
    }
    catch (HttpRequestException)
    {
        return Results.Json(
            new ErrorResponse(DownstreamUnreachable),
            statusCode: StatusCodes.Status502BadGateway);
    }
});

await app.RunAsync();

/// <summary>The /whoami body. The names are the wire contract, shared by every SDK.</summary>
internal sealed record WhoAmIResponse(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("tenant")] string Tenant,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("hasRead")] bool HasRead);

/// <summary>The /downstream body, carrying the callee's response verbatim.</summary>
internal sealed record DownstreamResponse(
    [property: JsonPropertyName("downstream")] string Downstream);

/// <summary>This example's own failure body. The code is not an SDK error code.</summary>
internal sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error);
