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
/// The issuer, JWKS endpoint and audience a verifier needs, from whichever source supplied them.
/// </summary>
/// <param name="Issuer">The expected <c>iss</c> claim.</param>
/// <param name="JwksUri">The JWKS endpoint.</param>
/// <param name="Audience">The expected <c>aud</c> claim, or empty to skip audience validation.</param>
internal sealed record IdentityCoordinates(string Issuer, string JwksUri, string Audience);
