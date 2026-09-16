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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Identity;

/// <summary>
/// Builds the fallback <see cref="JwksVerifier"/> once per application and hands the same
/// instance to every request.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton so one key-set cache serves every request, while the middleware
/// that uses it stays scoped and can therefore depend on a scoped
/// <see cref="ITokenVerifier"/> of the application's own.
/// </para>
/// <para>
/// The build runs behind a <see cref="Lazy{T}"/> of a task, which gives once-only execution,
/// a publication barrier and an awaitable result in one construct. A build fails only when no
/// issuer could be discovered or the JWKS endpoint is unusable — a configuration error rather
/// than a transient one — so the failed task is replayed to every later request instead of
/// retried on each one.
/// </para>
/// </remarks>
internal sealed class TokenVerifierProvider : IDisposable
{
    private readonly OAuthConfig _config;
    private readonly HttpClient? _httpClient;
    private readonly ILogger _logger;
    private readonly Lazy<Task<ITokenVerifier>> _verifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenVerifierProvider"/> class.
    /// </summary>
    /// <param name="options">The identity coordinates to build from.</param>
    /// <param name="logger">Receives discovery and warm-up diagnostics.</param>
    /// <param name="httpClientFactory">
    /// Supplies the client used for discovery and JWKS fetches, or <see langword="null"/> to
    /// use the shared default.
    /// </param>
    internal TokenVerifierProvider(
        IOptions<OAuthConfig> options,
        ILogger<TokenVerifierProvider> logger,
        IHttpClientFactory? httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        _config = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory?.CreateClient(
            DiagridIdentityServiceCollectionExtensions.HttpClientName);
        _verifier = new Lazy<Task<ITokenVerifier>>(BuildAsync);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenVerifierProvider"/> class that
    /// builds its verifier through <paramref name="build"/>.
    /// </summary>
    /// <remarks>
    /// The seam the fail-closed path is pinned through: every refusal
    /// <see cref="JwksVerifier.BuildAsync"/> makes is already an
    /// <see cref="IdentityNotConfiguredException"/>, so no configuration can drive the
    /// middleware's catch-all.
    /// </remarks>
    /// <param name="build">Produces the verifier, once.</param>
    internal TokenVerifierProvider(Func<Task<ITokenVerifier>> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        _config = new OAuthConfig();
        _logger = NullLogger.Instance;
        _verifier = new Lazy<Task<ITokenVerifier>>(build);
    }

    /// <summary>
    /// Returns the shared verifier, building it on the first call.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for an in-flight build.</param>
    /// <returns>The verifier.</returns>
    /// <exception cref="IdentityNotConfiguredException">
    /// No source supplied identity coordinates.
    /// </exception>
    internal async Task<ITokenVerifier> GetAsync(CancellationToken cancellationToken) =>
        // Waiting on the token rather than passing it into the build keeps one aborted
        // request from cancelling the build every other request is waiting on.
        await _verifier.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_verifier.IsValueCreated)
        {
            return;
        }

        var build = _verifier.Value;
        if (build.IsCompletedSuccessfully)
        {
            (build.Result as IDisposable)?.Dispose();
        }
    }

    private async Task<ITokenVerifier> BuildAsync() =>
        await JwksVerifier.BuildAsync(
                _config.Issuer,
                _config.Audience,
                _config.JwksUri,
                _httpClient,
                _logger,
                _config.AllowInsecureJwks,
                CancellationToken.None)
            .ConfigureAwait(false);
}
