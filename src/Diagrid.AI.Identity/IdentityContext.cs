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

namespace Diagrid.AI.Identity;

/// <summary>
/// The calling user's token, for the duration of one inbound request.
/// </summary>
/// <remarks>
/// <see cref="OAuthMiddleware"/> stores the raw inbound token here; the token rides an
/// <see cref="AsyncLocal{T}"/>, so concurrent requests each see only their own caller.
/// An application does not normally read it: outbound on-behalf-of calls go through the
/// client from
/// <see cref="DiagridIdentityHttpClientServiceCollectionExtensions.AddDiagridIdentityHttpClient"/>,
/// which reads the token at send time and sets <see cref="UserTokenHeader"/> itself.
/// </remarks>
public static class IdentityContext
{
    /// <summary>
    /// The header carrying the end-user token, inbound and outbound.
    /// </summary>
    public const string UserTokenHeader = "X-Diagrid-User-Token";

    /// <summary>
    /// The scheme prefix expected on <see cref="UserTokenHeader"/> values.
    /// </summary>
    public const string BearerPrefix = "Bearer ";

    private static readonly AsyncLocal<string?> CurrentToken = new();

    /// <summary>
    /// Gets the raw bearer token, or <see langword="null"/> outside an authenticated request.
    /// </summary>
    public static string? CurrentUserToken => CurrentToken.Value;

    /// <summary>
    /// Stores the raw bearer token for the current async flow.
    /// </summary>
    /// <param name="rawToken">The raw token, without the <see cref="BearerPrefix"/>.</param>
    /// <returns>
    /// A scope that restores the previous value when disposed. Dispose it rather than
    /// calling <see cref="ClearCurrentToken"/>, so a nested scope cannot erase the token
    /// an outer one is still relying on.
    /// </returns>
    public static IDisposable SetCurrentToken(string rawToken)
    {
        var previous = CurrentToken.Value;
        CurrentToken.Value = rawToken;
        return new TokenScope(previous);
    }

    /// <summary>
    /// Clears the token for the current async flow.
    /// </summary>
    public static void ClearCurrentToken() => CurrentToken.Value = null;

    /// <summary>
    /// Builds the headers to attach on outbound MCP / sub-agent calls.
    /// </summary>
    /// <remarks>
    /// Internal on purpose: an application that assembles this header itself has no origin
    /// guard, no cleared header and no send-time read.
    /// </remarks>
    /// <returns>
    /// An empty dictionary when there is no inbound user context (scheduled, pub/sub, cron
    /// triggers), so the header is omitted entirely rather than sent empty.
    /// </returns>
    internal static IReadOnlyDictionary<string, string> OutboundIdentityHeaders()
    {
        var token = CurrentToken.Value;
        if (string.IsNullOrEmpty(token))
        {
            return new Dictionary<string, string>();
        }

        return new Dictionary<string, string> { [UserTokenHeader] = BearerPrefix + token };
    }

    private sealed class TokenScope(string? previous) : IDisposable
    {
        public void Dispose() => CurrentToken.Value = previous;
    }
}
