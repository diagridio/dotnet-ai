# Dapr .NET SDK - Identity Demo

A two-route HTTP service that verifies the inbound Catalyst user token and propagates the
caller's identity on an outbound call. It is deliberately minimal: no agent, no workflow,
no state store — only `Diagrid.AI.Identity`.

## How it works

Two lines of app code install identity:

```csharp
builder.Services.AddDiagridIdentity();
...
app.UseDiagridIdentity();
```

A third registers the client outbound calls go out on:

```csharp
builder.Services.AddDiagridIdentityHttpClient();
```

`AddDiagridIdentity()` is called with no configuration, so the defaults apply: `RequireAuth`
stays `true`, no scopes are required, and the issuer, audience and JWKS URI are discovered
from the Catalyst sidecar's `/v1.0/metadata` endpoint.

The example demonstrates three things:

- **Inbound verification** — `app.UseDiagridIdentity()` sits ahead of both endpoints, so a
  request without a valid `X-Diagrid-User-Token` never reaches a handler.
- **Reading the caller** — `GET /whoami` calls `context.GetVerifiedUser()`, the typed
  accessor, and returns the subject, tenant, scopes and `HasScope("read")`. No cast out of
  `HttpContext.Items`. Because no scopes are required on the middleware, scope handling is
  shown inside the handler instead, which keeps the example runnable with any token.
- **Outbound propagation** — `GET /downstream` resolves the SDK-registered client from
  `IHttpClientFactory` and makes one ordinary HTTP GET to `DOWNSTREAM_URL` (default
  `http://localhost:8081/whoami`), so the callee sees the same caller. There is no header
  code in the handler: the client reads the inbound token at send time and sets
  `X-Diagrid-User-Token` itself. A failed outbound call returns 502
  `{"error":"downstream_unreachable"}` — that code is this example's own, not an SDK error
  code.

## Run it

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed — the version
  `global.json` pins and every example project targets
- [Diagrid CLI](https://docs.diagrid.io/catalyst/references/cli-reference/) and a Catalyst
  project

Run the app through the Catalyst sidecar from `\examples\IdentityDemo\`. It listens on
`http://localhost:5041`.

```sh
diagrid dev run -- dotnet run
```

`diagrid dev run` is what supplies the identity coordinates: it sets `DAPR_HTTP_ENDPOINT` (and
`DAPR_API_TOKEN`) so the SDK's remote `/v1.0/metadata` probe finds the issuer, audience and
JWKS endpoint. A plain `dapr run` against an OSS Dapr sidecar publishes no identity block, so
discovery finds nothing and every token-carrying request answers 503
`{"error":"oauth.not_configured"}` while a tokenless one still answers 401
`{"error":"oauth.missing_token"}`.

To point the outbound call somewhere else:

```sh
DOWNSTREAM_URL=http://localhost:9000/whoami dotnet run
```

## Try it

With a valid token:

```sh
curl -H "X-Diagrid-User-Token: Bearer $USER_TOKEN" http://localhost:5041/whoami
```

```json
{"subject":"alice@example.com","tenant":"acme","scopes":["read","write"],"hasRead":true}
```

```sh
curl -H "X-Diagrid-User-Token: Bearer $USER_TOKEN" http://localhost:5041/downstream
```

```json
{"downstream":"{\"subject\":\"alice@example.com\",\"tenant\":\"acme\",\"scopes\":[\"read\",\"write\"],\"hasRead\":true}"}
```

Every rejection uses the same one-field body, `{"error":"<code>"}`:

```sh
# No token at all
curl -i http://localhost:5041/whoami
```

```
HTTP/1.1 401 Unauthorized
{"error":"oauth.missing_token"}
```

```sh
# A token that is not a well-formed JWT
curl -i -H "X-Diagrid-User-Token: Bearer not-a-jwt" http://localhost:5041/whoami
```

```
HTTP/1.1 401 Unauthorized
{"error":"oauth.decode_error"}
```

```sh
# A well-formed token whose exp has passed
curl -i -H "X-Diagrid-User-Token: Bearer $EXPIRED_TOKEN" http://localhost:5041/whoami
```

```
HTTP/1.1 401 Unauthorized
{"error":"oauth.expired"}
```

If the outbound callee is not listening:

```
HTTP/1.1 502 Bad Gateway
{"error":"downstream_unreachable"}
```

## Notes

- The token arrives in the `X-Diagrid-User-Token` header, which the Catalyst sidecar sets on
  requests it forwards to your app. The client from `AddDiagridIdentityHttpClient()` puts the
  same header back on outbound calls. It is a plain `HttpClient` from `IHttpClientFactory`,
  so it can be handed to an MCP client or a generated API client the same way any other one
  can. The token is read per request rather than when the client is built, so one shared,
  long-lived client is safe under concurrency, and it only ever goes to the origin the
  handler addressed — a redirect to another host drops it, along with the application's own
  `Authorization`, `Cookie` and `Proxy-Authorization` headers. Those three are stripped by the
  SDK rather than by `HttpClient`, because the SDK is the one following the hop. Exhausting the
  redirect budget throws
  `HttpRequestException`, as `HttpClientHandler` does. An app that already owns a client it
  cannot replace can install `DiagridIdentityHandler` on it directly instead; give that
  client's primary handler `AllowAutoRedirect = false`, which is what lets the origin rule
  apply per hop. An app that would rather keep the platform's redirect behaviour passes
  `AddDiagridIdentityHttpClient(followRedirects: false)` and is warned at registration that
  the origin guard cannot fire for a hop the platform follows.
- `RequireAuth` governs the no-token case only. `RequireAuth = false` lets unauthenticated
  routes — health and readiness probes — share the same app: requests without a token then
  reach the handler and `GetVerifiedUser()` returns `null`. A token that **is** present is
  always verified either way, and an invalid one is always refused. This example leaves it at
  the fail-closed default.
- `AllowInsecureJwks = true` accepts a non-loopback plaintext `http://` JWKS endpoint. It
  relaxes plain HTTP and nothing else — `file://` and every other scheme stay refused — and it
  is a local-development escape hatch only. Do not set it in production: signing keys fetched
  over plaintext can be substituted by anyone on the path, which gives up the guarantee that a
  verified token was signed by dp-Sentry. Loopback endpoints are exempt without it.
- The full discovery precedence, the `{"error":"<code>"}` contract and the ten-row status/code
  table are in the [repository README](../../README.md#identity).
