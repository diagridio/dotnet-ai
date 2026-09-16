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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Diagrid.AI.Identity;

/// <summary>
/// Fetches JWKS from a public HTTPS endpoint, caches the keys, and verifies dp-Sentry JWTs.
/// </summary>
/// <remarks>
/// Thread-safe: the key set lives behind a
/// <see cref="ConfigurationManager{T}"/>, which swaps it atomically on refresh.
/// </remarks>
public sealed class JwksVerifier : ITokenVerifier, IDisposable
{
    /// <summary>
    /// Leeway applied to time-based claims, absorbing clock drift between the signer and this host.
    /// </summary>
    public const int ClockSkewSeconds = 120;

    /// <summary>
    /// How long a fetched key set is served before a background refresh.
    /// </summary>
    public const int JwksCacheLifetimeSeconds = 300;

    private const int PooledConnectionLifetimeMinutes = 2;
    private const int JwksRequestTimeoutSeconds = 30;

    /// <summary>
    /// Shortest interval between two key-set fetches triggered by an unrecognised <c>kid</c>.
    /// Matches the refresh interval <see cref="ConfigurationManager{T}"/> rate-limits itself by.
    /// </summary>
    private const int SigningKeyRefreshIntervalSeconds = 30;

    private static readonly string[] SupportedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.EcdsaSha256,
    ];

    private static readonly string[] RequiredClaims = ["exp", "iss", "sub"];

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _jwksUri;
    private readonly ILogger _logger;
    private readonly IDocumentRetriever _documentRetriever;
    private readonly IConfigurationRetriever<OpenIdConnectConfiguration> _configurationRetriever = new JwksConfigurationRetriever();
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private DateTimeOffset _nextDirectRefresh = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwksVerifier"/> class.
    /// </summary>
    /// <param name="issuer">The expected <c>iss</c> claim; empty skips issuer validation.</param>
    /// <param name="jwksUri">The JWKS endpoint to fetch signing keys from.</param>
    /// <param name="audience">The expected <c>aud</c> claim; empty skips audience validation.</param>
    /// <param name="httpClient">
    /// The client used to fetch the JWKS. A shared default is used when <see langword="null"/>.
    /// </param>
    /// <param name="logger">Receives warm-up warnings.</param>
    /// <param name="allowInsecureJwks">
    /// Whether a non-loopback <c>http://</c> endpoint is accepted. Off by default: key
    /// material fetched over plaintext can be substituted by anyone on the path. It relaxes
    /// plain HTTP only — <c>file://</c> and every other scheme stay refused.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="jwksUri"/> is <see langword="null"/> or empty, which no configuration
    /// source can produce: the caller passed nothing.
    /// </exception>
    /// <exception cref="IdentityNotConfiguredException">
    /// <paramref name="jwksUri"/> is unusable: not an absolute URI, a scheme other than
    /// <c>http</c> or <c>https</c>, or a non-loopback <c>http://</c> endpoint without
    /// <paramref name="allowInsecureJwks"/>. The middleware maps this to 503
    /// <see cref="OAuthErrorCodes.NotConfigured"/>.
    /// </exception>
    public JwksVerifier(
        string issuer,
        string jwksUri,
        string audience = "",
        HttpClient? httpClient = null,
        ILogger? logger = null,
        bool allowInsecureJwks = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(jwksUri);

        _issuer = issuer ?? string.Empty;
        _audience = audience ?? string.Empty;
        _jwksUri = jwksUri;
        _logger = logger ?? NullLogger.Instance;

        var requireHttps = ResolveRequireHttps(jwksUri, allowInsecureJwks);
        _documentRetriever = new HttpDocumentRetriever(httpClient ?? SharedHttpClient.Instance)
        {
            RequireHttps = requireHttps,
        };

        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            jwksUri,
            _configurationRetriever,
            _documentRetriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromSeconds(JwksCacheLifetimeSeconds),
        };
    }

    /// <summary>
    /// Builds a verifier from explicit configuration, local sidecar metadata, remote sidecar
    /// metadata, or environment variables, in that order of precedence, and warms its key set.
    /// </summary>
    /// <param name="issuer">The expected <c>iss</c> claim, or <see langword="null"/> to discover it.</param>
    /// <param name="audience">The expected <c>aud</c> claim, or <see langword="null"/> to discover it.</param>
    /// <param name="jwksUri">The JWKS endpoint, or <see langword="null"/> to discover it.</param>
    /// <param name="httpClient">The client used for discovery and JWKS fetches.</param>
    /// <param name="logger">Receives discovery and warm-up diagnostics.</param>
    /// <param name="allowInsecureJwks">
    /// Whether a non-loopback <c>http://</c> JWKS endpoint is accepted. Off by default.
    /// </param>
    /// <param name="cancellationToken">Cancels discovery and warm-up.</param>
    /// <returns>A warmed verifier.</returns>
    /// <exception cref="IdentityNotConfiguredException">
    /// No source supplied an issuer and JWKS endpoint.
    /// </exception>
    public static async Task<JwksVerifier> BuildAsync(
        string? issuer = null,
        string? audience = null,
        string? jwksUri = null,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        bool allowInsecureJwks = false,
        CancellationToken cancellationToken = default)
    {
        var client = httpClient ?? SharedHttpClient.Instance;

        IdentityCoordinates? discovered = null;
        if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(jwksUri))
        {
            // The local sidecar is asked first so a deployed in-cluster app keeps its
            // loopback call rather than paying for a network round trip.
            discovered =
                await IdentityDiscovery.DiscoverFromMetadataAsync(client, logger, cancellationToken)
                    .ConfigureAwait(false)
                ?? await IdentityDiscovery.DiscoverFromRemoteAsync(client, logger, cancellationToken)
                    .ConfigureAwait(false)
                ?? IdentityDiscovery.DiscoverFromEnvironment();
        }

        var resolvedIssuer = string.IsNullOrEmpty(issuer) ? discovered?.Issuer ?? string.Empty : issuer;

        // Explicit, then discovered, then derived. A sidecar that publishes a jwks_uri away
        // from its issuer must not have it replaced by issuer + /jwks.json, and the discovered
        // value is adopted only when the discovered issuer is the one that resolved: without
        // that guard, a token minted by the advertised issuer but claiming the pinned one
        // would verify.
        var resolvedJwksUri = jwksUri ?? string.Empty;
        if (string.IsNullOrEmpty(resolvedJwksUri)
            && discovered is not null
            && string.Equals(discovered.Issuer, resolvedIssuer, StringComparison.Ordinal))
        {
            resolvedJwksUri = discovered.JwksUri;
        }

        if (string.IsNullOrEmpty(resolvedJwksUri) && !string.IsNullOrEmpty(resolvedIssuer))
        {
            resolvedJwksUri = IdentityDiscovery.DefaultJwksUri(resolvedIssuer);
        }

        var resolvedAudience = string.IsNullOrEmpty(audience) ? discovered?.Audience ?? string.Empty : audience;

        if (string.IsNullOrEmpty(resolvedIssuer) || string.IsNullOrEmpty(resolvedJwksUri))
        {
            throw new IdentityNotConfiguredException(
                "Cannot discover identity coordinates: "
                + "set issuer/jwks_uri explicitly, configure the sidecar metadata endpoint "
                + "(" + IdentityDiscovery.DaprHttpPortVariable + " locally or "
                + IdentityDiscovery.DaprHttpEndpointVariable + " for a remote sidecar), "
                + "or set " + IdentityDiscovery.IssuerVariable);
        }

        var verifier = new JwksVerifier(
            resolvedIssuer,
            resolvedJwksUri,
            resolvedAudience,
            client,
            logger,
            allowInsecureJwks);
        await verifier.WarmAsync(cancellationToken).ConfigureAwait(false);
        return verifier;
    }

    /// <summary>
    /// Eagerly fetches the JWKS so the first verify call does not block.
    /// </summary>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>A task that completes once the attempt has finished, successfully or not.</returns>
    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "JWKS warm-up failed; will retry on first request");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object>> VerifyAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var token = ParseToken(rawToken);

        var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        // Exactly one key is resolved by `kid` before decoding: an unrecognised `kid` means
        // our key material is stale, not that the token is bad, so it surfaces as "not ready"
        // (503) rather than a rejection (401).
        var signingKey = await ResolveSigningKeyAsync(configuration, token.Kid, cancellationToken)
            .ConfigureAwait(false);

        if (!SupportedAlgorithms.Contains(token.Alg, StringComparer.Ordinal))
        {
            throw new TokenVerificationException(
                OAuthErrorCodes.InvalidToken,
                "the specified alg value is not allowed");
        }

        var result = await TokenHandler.ValidateTokenAsync(rawToken, BuildValidationParameters(signingKey))
            .ConfigureAwait(false);

        if (!result.IsValid)
        {
            // The required claims are checked after the signature but before exp, iss and
            // aud, so a token missing one reports invalid_token rather than whichever claim
            // check happened to fire first.
            if (IsClaimValidationFailure(result.Exception))
            {
                EnsureRequiredClaims(token);
                EnsureIssuerBeforeAudience(token, result.Exception);
            }

            throw MapValidationFailure(result.Exception);
        }

        EnsureRequiredClaims(token);

        // TokenValidationResult.Claims is declared IDictionary<string, object>: taking a
        // read-only view keeps a success from depending on its runtime type.
        return result.Claims.AsReadOnly();
    }

    /// <summary>
    /// Releases the refresh gate, and the configuration manager if it holds resources.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigurationManager{T}"/> does not implement <see cref="IDisposable"/>
    /// today, so the cast is what keeps its internal locks from leaking should a later version
    /// start owning disposable state. The <see cref="HttpClient"/> is left alone: the verifier
    /// only borrows it.
    /// </remarks>
    public void Dispose()
    {
        (_configurationManager as IDisposable)?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _configurationManager.GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new VerifierNotReadyException(ex.Message, ex);
        }
    }

    private async Task<SecurityKey> ResolveSigningKeyAsync(
        OpenIdConnectConfiguration configuration,
        string? kid,
        CancellationToken cancellationToken)
    {
        var key = FindSigningKey(configuration, kid);
        if (key is not null)
        {
            return key;
        }

        // A `kid` we do not know usually means the signer rotated, so re-read the key set
        // before giving up rather than serving 503 until the cache lifetime elapses.
        // RequestRefresh brings the manager's own cache along for later requests, but it
        // refreshes in the background once it holds a configuration, so this request fetches
        // the document itself.
        _configurationManager.RequestRefresh();
        var refreshed = await RefreshSigningKeysAsync(cancellationToken).ConfigureAwait(false);

        return (refreshed is null ? null : FindSigningKey(refreshed, kid))
            ?? throw new VerifierNotReadyException(
                $"Unable to find a signing key that matches: \"{kid}\"");
    }

    /// <summary>
    /// Fetches the key set directly, at most once every
    /// <see cref="SigningKeyRefreshIntervalSeconds"/>, so a burst of unrecognised
    /// <c>kid</c>s cannot amplify into a burst of JWKS fetches.
    /// </summary>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>
    /// The freshly fetched configuration, or <see langword="null"/> when the rate limit
    /// refused the fetch.
    /// </returns>
    private async Task<OpenIdConnectConfiguration?> RefreshSigningKeysAsync(
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextDirectRefresh)
            {
                return null;
            }

            _nextDirectRefresh = now.AddSeconds(SigningKeyRefreshIntervalSeconds);
            return await _configurationRetriever
                .GetConfigurationAsync(_jwksUri, _documentRetriever, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new VerifierNotReadyException(ex.Message, ex);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static JsonWebToken ParseToken(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);

        try
        {
            return new JsonWebToken(rawToken);
        }
        catch (Exception ex) when (ex is SecurityTokenMalformedException or ArgumentException)
        {
            throw new TokenVerificationException(OAuthErrorCodes.DecodeError, ex.Message, ex);
        }
    }

    private static SecurityKey? FindSigningKey(OpenIdConnectConfiguration configuration, string? kid) =>
        configuration.SigningKeys
            .FirstOrDefault(candidate => string.Equals(candidate.KeyId, kid, StringComparison.Ordinal));

    /// <summary>
    /// Decides whether the JWKS endpoint must be fetched over HTTPS.
    /// </summary>
    /// <param name="jwksUri">The configured endpoint.</param>
    /// <param name="allowInsecureJwks">Whether the app opted into plaintext retrieval.</param>
    /// <returns><see langword="true"/> when plaintext retrieval is refused.</returns>
    /// <exception cref="IdentityNotConfiguredException">
    /// <paramref name="jwksUri"/> is unusable: not an absolute URI, a scheme other than
    /// <c>http</c> or <c>https</c>, or a non-loopback <c>http://</c> endpoint without
    /// <paramref name="allowInsecureJwks"/>.
    /// </exception>
    /// <remarks>
    /// Every refusal here is a configuration outcome rather than a caller-contract violation,
    /// so it is reported as the not-configured failure the middleware maps to 503
    /// <see cref="OAuthErrorCodes.NotConfigured"/>.
    /// </remarks>
    private static bool ResolveRequireHttps(string jwksUri, bool allowInsecureJwks)
    {
        if (!Uri.TryCreate(jwksUri, UriKind.Absolute, out var uri))
        {
            throw new IdentityNotConfiguredException(
                $"JWKS endpoint \"{jwksUri}\" is not an absolute URI.");
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Refused however AllowInsecureJwks is set: opting into plaintext says nothing about
        // loading signing keys off the local filesystem or out of a directory server.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new IdentityNotConfiguredException(
                $"JWKS endpoint \"{jwksUri}\" must use HTTPS: the \"{uri.Scheme}\" scheme "
                + "cannot serve a key set this verifier will trust, and AllowInsecureJwks "
                + "relaxes plain HTTP only.");
        }

        // The loopback sidecar advertises a plain-HTTP issuer, and nothing on that path can
        // substitute the key set.
        if (allowInsecureJwks || uri.IsLoopback)
        {
            return false;
        }

        throw new IdentityNotConfiguredException(
            $"JWKS endpoint \"{jwksUri}\" must use HTTPS: signing keys fetched over "
            + "plaintext can be substituted in transit. Only loopback endpoints are exempt; "
            + "set AllowInsecureJwks to accept the risk deliberately.");
    }

    private TokenValidationParameters BuildValidationParameters(SecurityKey signingKey) => new()
    {
        ValidAlgorithms = SupportedAlgorithms,
        IssuerSigningKey = signingKey,
        TryAllIssuerSigningKeys = false,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds),
        ValidateIssuer = !string.IsNullOrEmpty(_issuer),
        ValidIssuer = _issuer,
        ValidateAudience = !string.IsNullOrEmpty(_audience),
        ValidAudience = _audience,
    };

    private static void EnsureRequiredClaims(JsonWebToken token)
    {
        foreach (var claim in RequiredClaims)
        {
            if (!token.TryGetPayloadValue<object>(claim, out _))
            {
                throw new TokenVerificationException(
                    OAuthErrorCodes.InvalidToken,
                    $"Token is missing the \"{claim}\" claim");
            }
        }
    }

    /// <summary>
    /// Reports an issuer mismatch ahead of an audience mismatch, whichever order the library
    /// found them in.
    /// </summary>
    /// <remarks>
    /// The contract reports required claims, then <c>exp</c>, then <c>iss</c>, then
    /// <c>aud</c>, so a token with two defects yields one code wherever it is sent.
    /// <see cref="JsonWebTokenHandler"/> validates lifetime, audience, then issuer and stops
    /// at the first failure, so only the last two need swapping — done by re-checking the
    /// issuer here rather than by validating every claim by hand.
    /// </remarks>
    /// <param name="token">The parsed token.</param>
    /// <param name="exception">The failure the library reported.</param>
    /// <exception cref="TokenVerificationException">
    /// The library reported an audience mismatch and the issuer does not match either.
    /// </exception>
    private void EnsureIssuerBeforeAudience(JsonWebToken token, Exception? exception)
    {
        if (exception is not SecurityTokenInvalidAudienceException
            || string.IsNullOrEmpty(_issuer)
            || string.Equals(token.Issuer, _issuer, StringComparison.Ordinal))
        {
            return;
        }

        throw new TokenVerificationException(
            OAuthErrorCodes.InvalidIssuer, "issuer mismatch", exception);
    }

    /// <summary>
    /// Reports whether a validation failure is a claim check rather than a signature or
    /// decoding failure, which are reported ahead of any claim.
    /// </summary>
    /// <param name="exception">The validation failure.</param>
    /// <returns><see langword="true"/> for a claim-level failure.</returns>
    private static bool IsClaimValidationFailure(Exception? exception) => exception is
        SecurityTokenExpiredException or
        SecurityTokenNotYetValidException or
        SecurityTokenInvalidLifetimeException or
        SecurityTokenInvalidIssuerException or
        SecurityTokenInvalidAudienceException;

    private static TokenVerificationException MapValidationFailure(Exception? exception) => exception switch
    {
        SecurityTokenExpiredException => new TokenVerificationException(
            OAuthErrorCodes.Expired, "token has expired", exception),
        SecurityTokenInvalidIssuerException => new TokenVerificationException(
            OAuthErrorCodes.InvalidIssuer, "issuer mismatch", exception),
        SecurityTokenInvalidAudienceException => new TokenVerificationException(
            OAuthErrorCodes.InvalidAudience, "audience mismatch", exception),
        SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException =>
            new TokenVerificationException(
                OAuthErrorCodes.InvalidSignature, "signature verification failed", exception),
        SecurityTokenMalformedException => new TokenVerificationException(
            OAuthErrorCodes.DecodeError, exception.Message, exception),
        _ => new TokenVerificationException(
            OAuthErrorCodes.InvalidToken, exception?.Message, exception),
    };

    /// <summary>
    /// Builds the handler behind <see cref="SharedHttpClient"/>.
    /// </summary>
    /// <remarks>
    /// A pooled-connection lifetime is what lets a long-lived client observe a DNS change
    /// for the JWKS host; the default handler holds its connections open indefinitely.
    /// </remarks>
    /// <returns>A handler that recycles pooled connections.</returns>
    internal static SocketsHttpHandler CreateSharedHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(PooledConnectionLifetimeMinutes),
    };

    internal static class SharedHttpClient
    {
        internal static HttpClient Instance { get; } = new(CreateSharedHandler())
        {
            Timeout = TimeSpan.FromSeconds(JwksRequestTimeoutSeconds),
        };
    }
}
