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

using System.Text.Json.Serialization;

namespace Diagrid.AI.Identity;

/// <summary>
/// The body of a rejected request: one field, carrying one of the
/// <see cref="OAuthErrorCodes"/> values and nothing that could leak token detail.
/// </summary>
/// <param name="Error">The error code.</param>
internal sealed record OAuthErrorResponse([property: JsonPropertyName("error")] string Error);
