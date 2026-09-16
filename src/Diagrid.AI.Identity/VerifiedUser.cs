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

namespace Diagrid.AI.Identity;

/// <summary>
/// Verified caller identity attached to <c>HttpContext.Items["diagrid.user"]</c>.
/// </summary>
/// <remarks>
/// Read it with
/// <see cref="DiagridIdentityHttpContextExtensions.GetVerifiedUser(HttpContext)"/> rather
/// than indexing <see cref="HttpContext.Items"/> and casting.
/// </remarks>
public sealed record VerifiedUser
{
    private static readonly ImmutableSortedSet<string> NoScopes =
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the <c>sub</c> claim — email, user-id, or agent SPIFFE URI.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the tenant / org claim extracted from the token.
    /// </summary>
    public string Tenant { get; init; } = string.Empty;

    /// <summary>
    /// Gets the OAuth scopes carried by the token, in ordinal order.
    /// </summary>
    /// <remarks>
    /// Sorted rather than hashed so a handler echoing the set into a response body emits the
    /// same order on every host: a hash set's enumeration order is arbitrary, and a
    /// culture-sensitive comparer's is not portable. Use <see cref="HasScope"/> to test for
    /// one scope.
    /// </remarks>
    public IReadOnlySet<string> Scopes { get; init; } = NoScopes;

    /// <summary>
    /// Gets the full verified JWT payload for policies that need richer access.
    /// </summary>
    public IReadOnlyDictionary<string, object> Claims { get; init; } =
        ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Gets the <c>iss</c> value on the verified token.
    /// </summary>
    public string IssuerId { get; init; } = string.Empty;

    /// <summary>
    /// Reports whether the token carries <paramref name="scope"/>.
    /// </summary>
    /// <remarks>
    /// Case-sensitive: a scope is an opaque string the signer issued, and folding case here
    /// would grant one it did not.
    /// </remarks>
    /// <param name="scope">The scope to look for.</param>
    /// <returns><see langword="true"/> when the scope is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    public bool HasScope(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Scopes.Contains(scope);
    }
}
