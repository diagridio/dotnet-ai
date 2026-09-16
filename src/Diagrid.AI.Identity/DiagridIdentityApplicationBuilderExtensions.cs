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

using Microsoft.AspNetCore.Builder;

namespace Diagrid.AI.Identity;

/// <summary>
/// Pipeline extensions for inbound user-token verification.
/// </summary>
public static class DiagridIdentityApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="OAuthMiddleware"/> to the request pipeline.
    /// </summary>
    /// <remarks>
    /// Place it before the endpoints that need a verified caller. Call
    /// <see cref="DiagridIdentityServiceCollectionExtensions.AddDiagridIdentity"/> first —
    /// the middleware is resolved from the container.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseDiagridIdentity(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<OAuthMiddleware>();
    }
}
