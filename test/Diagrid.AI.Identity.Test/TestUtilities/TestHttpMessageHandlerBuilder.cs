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

using Microsoft.Extensions.Http;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// The builder <see cref="IHttpMessageHandlerFactory"/> would hand a
/// <see cref="IHttpMessageHandlerBuilderFilter"/>, so a test can see what the filter did.
/// </summary>
internal sealed class TestHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
{
    /// <inheritdoc />
    public override string? Name { get; set; }

    /// <inheritdoc />
    public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

    /// <inheritdoc />
    public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

    /// <inheritdoc />
    public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
}
