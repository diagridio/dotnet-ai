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
/// Per-client opt-in to outbound identity, keyed by <see cref="HttpClient"/> name.
/// </summary>
/// <remarks>
/// Named options rather than a registry the registration mutates, so
/// <see cref="DiagridIdentityHandlerFilter"/> — which sees every client the application
/// builds, not only the Diagrid ones — can tell them apart without shared mutable state.
/// </remarks>
internal sealed class DiagridIdentityHttpClientOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether this client carries outbound identity.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether
    /// <see cref="DiagridIdentityHandler"/> follows redirects itself.
    /// </summary>
    /// <remarks>
    /// On by default, because following the hops here is the only way the origin guard can
    /// fire. Turning it off leaves <see cref="HttpClientHandler.AllowAutoRedirect"/> alone and
    /// warns that a hop the platform follows carries the identity header past the origin.
    /// </remarks>
    public bool FollowRedirects { get; set; } = true;
}
