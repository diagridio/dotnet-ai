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

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Identity;

/// <summary>
/// Verifies <c>X-Diagrid-User-Token</c> on every inbound request.
/// </summary>
/// <remarks>
/// <para>
/// The verified caller is attached to <c>HttpContext.Items["diagrid.user"]</c> as a
/// <see cref="VerifiedUser"/>, and read back with
/// <see cref="DiagridIdentityHttpContextExtensions.GetVerifiedUser(HttpContext)"/>.
/// <c>HttpContext.User</c> is left alone: it belongs to the application's own
/// authentication.
/// </para>
/// <para>
/// Usage:
/// <code>
/// builder.Services.AddDiagridIdentity(cfg =&gt; cfg.Scopes = ["agent.invoke"]);
/// app.UseDiagridIdentity();
///
/// app.MapPost("/invoke", (HttpContext ctx) =&gt;
/// {
///     var user = ctx.GetVerifiedUser()!;
///     return Results.Ok(new { user.Subject });
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class OAuthMiddleware : IMiddleware
{
    /// <summary>
    /// The <see cref="HttpContext.Items"/> key carrying the <see cref="VerifiedUser"/>.
    /// </summary>
    /// <remarks>
    /// Namespaced because <see cref="HttpContext.Items"/> is one dictionary shared by every
    /// middleware in the pipeline. Prefer
    /// <see cref="DiagridIdentityHttpContextExtensions.GetVerifiedUser(HttpContext)"/>,
    /// which needs no cast.
    /// </remarks>
    public const string VerifiedUserItemKey = "diagrid.user";

    private const string CacheControlHeaderValue = "no-store";
    private const string SubjectClaim = "sub";
    private const string IssuerClaim = "iss";
    private const string TenantIdClaim = "tid";
    private const string TenantClaim = "tenant";
    private static readonly string[] ScopeClaims = ["scp", "scope", "scopes"];

    private static readonly ImmutableSortedSet<string> EmptyScopes =
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    private readonly ImmutableHashSet<string> _requiredScopes;
    private readonly bool _requireAuth;
    private readonly ITokenVerifier? _registeredVerifier;
    private readonly TokenVerifierProvider _verifierProvider;
    private readonly ILogger<OAuthMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthMiddleware"/> class.
    /// </summary>
    /// <param name="options">The policy to enforce.</param>
    /// <param name="registeredVerifier">
    /// A verifier from the container, or <see langword="null"/> to fall back to
    /// <paramref name="verifierProvider"/>.
    /// </param>
    /// <param name="verifierProvider">
    /// Builds a verifier from discovered identity coordinates on the first authenticated
    /// request and shares it across requests.
    /// </param>
    /// <param name="logger">Receives configuration diagnostics.</param>
    internal OAuthMiddleware(
        IOptions<OAuthConfig> options,
        ITokenVerifier? registeredVerifier,
        TokenVerifierProvider verifierProvider,
        ILogger<OAuthMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = options.Value;
        _requiredScopes = config.Scopes.ToImmutableHashSet(StringComparer.Ordinal);
        _requireAuth = config.RequireAuth;
        _registeredVerifier = registeredVerifier;
        _verifierProvider = verifierProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var token = TrimBearer(context.Request.Headers[IdentityContext.UserTokenHeader].ToString());

        if (token.Length == 0)
        {
            if (_requireAuth)
            {
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, OAuthErrorCodes.MissingToken)
                    .ConfigureAwait(false);
                return;
            }

            IdentityContext.ClearCurrentToken();
            await next(context).ConfigureAwait(false);
            return;
        }

        ITokenVerifier verifier;
        try
        {
            verifier = await GetVerifierAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsClientDisconnect(ex, context))
        {
            // Any build failure, not only IdentityNotConfiguredException: an exception that
            // escapes here leaves the pipeline as a framework 500 with no {"error"} body and
            // no Cache-Control.
            _logger.LogWarning(ex, "identity verifier not configured; rejecting request");
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, OAuthErrorCodes.NotConfigured)
                .ConfigureAwait(false);
            return;
        }

        IReadOnlyDictionary<string, object> payload;
        ImmutableSortedSet<string> scopes;
        try
        {
            payload = await verifier.VerifyAsync(token, context.RequestAborted).ConfigureAwait(false);
            scopes = ExtractScopes(payload);
        }
        catch (VerifierNotReadyException)
        {
            await WriteErrorAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    OAuthErrorCodes.VerifierUnavailable)
                .ConfigureAwait(false);
            return;
        }
        catch (TokenVerificationException ex)
        {
            var status = string.Equals(ex.Code, OAuthErrorCodes.MissingScope, StringComparison.Ordinal)
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            await WriteErrorAsync(context, status, ex.Code).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (!IsClientDisconnect(ex, context))
        {
            // Ordered last: ahead of the two clauses above it would answer 503 for a token
            // that was correctly rejected, hiding a 401 or a missing-scope 403. An
            // unanticipated failure means no caller can be adjudicated at all, so the token is
            // not what is at fault.
            _logger.LogWarning(ex, "unexpected identity verification failure; rejecting request");
            await WriteErrorAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    OAuthErrorCodes.VerifierUnavailable)
                .ConfigureAwait(false);
            return;
        }

        if (!_requiredScopes.IsSubsetOf(scopes))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, OAuthErrorCodes.MissingScope)
                .ConfigureAwait(false);
            return;
        }

        context.Items[VerifiedUserItemKey] = new VerifiedUser
        {
            Subject = ReadString(payload, SubjectClaim),
            Tenant = payload.ContainsKey(TenantIdClaim)
                ? ReadString(payload, TenantIdClaim)
                : ReadString(payload, TenantClaim),
            Scopes = scopes,
            Claims = payload,
            IssuerId = ReadString(payload, IssuerClaim),
        };

        using var tokenScope = IdentityContext.SetCurrentToken(token);
        await next(context).ConfigureAwait(false);
    }

    private async Task<ITokenVerifier> GetVerifierAsync(CancellationToken cancellationToken) =>
        _registeredVerifier
        ?? await _verifierProvider.GetAsync(cancellationToken).ConfigureAwait(false);

    private static string TrimBearer(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith(IdentityContext.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[IdentityContext.BearerPrefix.Length..];
        }

        return trimmed.Trim();
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is the caller going away rather than a failure.
    /// </summary>
    /// <remarks>
    /// The exception type alone cannot tell the two apart: a verifier's own HTTP timeout also
    /// surfaces as <see cref="TaskCanceledException"/>. Only a signalled
    /// <see cref="HttpContext.RequestAborted"/> means the client disconnected, and that one has
    /// to reach ASP.NET rather than become a 503 nobody is listening for.
    /// </remarks>
    private static bool IsClientDisconnect(Exception exception, HttpContext context) =>
        exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested;

    private static ImmutableSortedSet<string> ExtractScopes(IReadOnlyDictionary<string, object> payload)
    {
        // An absent *or empty* claim moves on to the next spelling, so "scp": [] does not
        // mask a populated "scope".
        foreach (var claim in ScopeClaims)
        {
            if (!payload.TryGetValue(claim, out var raw))
            {
                continue;
            }

            var scopes = NormalizeScopes(raw);
            if (scopes.Count > 0)
            {
                return scopes;
            }
        }

        return EmptyScopes;
    }

    /// <summary>
    /// Normalizes a scope claim into an ordinally sorted set.
    /// </summary>
    /// <remarks>
    /// Sorted rather than hashed because <see cref="VerifiedUser.Scopes"/> is something a
    /// handler echoes back, and a hash set's enumeration order is arbitrary: the same token
    /// has to serialize the same way on every host and from every SDK.
    /// </remarks>
    /// <param name="raw">The claim value, a space-delimited string or a list.</param>
    /// <returns>The scopes.</returns>
    private static ImmutableSortedSet<string> NormalizeScopes(object? raw) => raw switch
    {
        string single => single
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableSortedSet(StringComparer.Ordinal),
        IEnumerable<object> many => many
            .Select(item => item?.ToString())
            .Where(item => !string.IsNullOrEmpty(item))
            .ToImmutableSortedSet(StringComparer.Ordinal)!,
        _ => EmptyScopes,
    };

    private static string ReadString(IReadOnlyDictionary<string, object> payload, string claim) =>
        payload.TryGetValue(claim, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static Task WriteErrorAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = CacheControlHeaderValue;
        return context.Response.WriteAsJsonAsync(new OAuthErrorResponse(code), context.RequestAborted);
    }
}
