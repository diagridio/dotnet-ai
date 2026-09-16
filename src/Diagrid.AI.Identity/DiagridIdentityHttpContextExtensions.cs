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

using Microsoft.AspNetCore.Http;

namespace Diagrid.AI.Identity;

/// <summary>
/// Request extensions for reading the verified caller.
/// </summary>
public static class DiagridIdentityHttpContextExtensions
{
    /// <summary>
    /// Returns the caller <see cref="OAuthMiddleware"/> verified on this request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The typed way to read what the middleware attached: <see cref="HttpContext.Items"/> is
    /// a string-keyed bag of <see cref="object"/>, so reading
    /// <see cref="OAuthMiddleware.VerifiedUserItemKey"/> directly costs an unchecked cast.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// app.MapPost("/invoke", (HttpContext ctx) =&gt;
    /// {
    ///     var user = ctx.GetVerifiedUser();
    ///     return user is null ? Results.Unauthorized() : Results.Ok(new { user.Subject });
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="context">The current request.</param>
    /// <returns>
    /// The verified caller, or <see langword="null"/> on a request that carried no token —
    /// which only reaches a handler when <see cref="OAuthConfig.RequireAuth"/> is
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static VerifiedUser? GetVerifiedUser(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(OAuthMiddleware.VerifiedUserItemKey, out var value)
            ? value as VerifiedUser
            : null;
    }
}
