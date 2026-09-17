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

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// A verifier with a scripted answer, so middleware tests exercise the pipeline rather than
/// the crypto — which <see cref="JwksVerifierTests"/> already covers.
/// </summary>
internal sealed class FakeTokenVerifier : ITokenVerifier
{
    private readonly IReadOnlyDictionary<string, object>? _payload;
    private readonly Exception? _failure;
    private readonly bool _waitForCancellation;

    private FakeTokenVerifier(
        IReadOnlyDictionary<string, object>? payload,
        Exception? failure,
        bool waitForCancellation = false)
    {
        _payload = payload;
        _failure = failure;
        _waitForCancellation = waitForCancellation;
    }

    /// <summary>
    /// Gets the raw token the middleware handed over, or <see langword="null"/> if it never called.
    /// </summary>
    internal string? LastToken { get; private set; }

    /// <summary>
    /// Builds a verifier that accepts any token and returns the given claims.
    /// </summary>
    /// <param name="claims">The claims to return; a minimal valid payload when omitted.</param>
    /// <returns>The verifier.</returns>
    internal static FakeTokenVerifier Accepting(IReadOnlyDictionary<string, object>? claims = null) =>
        new(claims ?? DefaultClaims(), null);

    /// <summary>
    /// Builds a verifier that rejects every token with the given failure.
    /// </summary>
    /// <param name="failure">The exception to throw.</param>
    /// <returns>The verifier.</returns>
    internal static FakeTokenVerifier Failing(Exception failure) => new(null, failure);

    /// <summary>
    /// Builds a verifier that never answers until the request is aborted.
    /// </summary>
    /// <remarks>
    /// The only way to exercise a real client disconnect: it waits on the token the middleware
    /// passes down, which is <see cref="HttpContext.RequestAborted"/>. Throwing a bare
    /// <see cref="OperationCanceledException"/> instead would not signal that token, and so
    /// cannot be told apart from a verifier's own timeout.
    /// </remarks>
    /// <returns>The verifier.</returns>
    internal static FakeTokenVerifier WaitingForCancellation() => new(null, null, waitForCancellation: true);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object>> VerifyAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        LastToken = rawToken;

        if (_waitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return await (_failure is not null
            ? Task.FromException<IReadOnlyDictionary<string, object>>(_failure)
            : Task.FromResult(_payload!)).ConfigureAwait(false);
    }

    private static Dictionary<string, object> DefaultClaims() => new(StringComparer.Ordinal)
    {
        ["sub"] = TokenFactory.Subject,
        ["iss"] = TokenFactory.Issuer,
        ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
    };
}
