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

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Diagrid.AI.Identity;

/// <summary>
/// Reads a bare JWKS document into an <see cref="OpenIdConnectConfiguration"/>.
/// </summary>
/// <remarks>
/// dp-Sentry publishes the key set directly at the JWKS endpoint, so there is no
/// <c>/.well-known/openid-configuration</c> to walk — which is what the stock
/// <see cref="OpenIdConnectConfigurationRetriever"/> would expect. This retriever exists
/// only to let <see cref="ConfigurationManager{T}"/> supply its caching and refresh around
/// that endpoint.
/// </remarks>
internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(retriever);

        var document = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
        var keySet = new JsonWebKeySet(document);

        var configuration = new OpenIdConnectConfiguration
        {
            JwksUri = address,
            JsonWebKeySet = keySet,
        };

        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
