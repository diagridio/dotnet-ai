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


using Dapr.Workflow.Payloads;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

public static partial class DaprAgentsBuilderExtensions
{
    // =========================================================================
    // Binary store payloads
    //
    // Every agent turn carries the whole conversation across activity boundaries (CallLlmInput,
    // AgentRunResult, ...), and each of those payloads is stored in the workflow history. Dapr 1.19
    // can keep large payloads in a binary store and write only a reference into the history; the
    // SDK uploads and resolves them transparently, so workflows and activities are unchanged.
    // =========================================================================

    /// <summary>
    /// Keeps workflow payloads larger than <see cref="BinaryStorePayloadOptions.OffloadThreshold"/>
    /// (64 KiB by default) in the named Dapr binary store, writing only a reference into the workflow
    /// history. Long agent conversations are the payloads this is meant for.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="storeName">The name of the Dapr binary store component.</param>
    /// <param name="configure">Optional further configuration, such as the threshold or key prefix.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// <para>
    /// Sets <see cref="BinaryStorePayloadOptions.WritePreference"/> to
    /// <see cref="PayloadWritePreference.PreferReference"/>, because the SDK default
    /// (<see cref="PayloadWritePreference.Preserve"/>) offloads nothing automatically. <paramref name="configure"/>
    /// runs afterwards and may change it.
    /// </para>
    /// <para>
    /// Requires a Dapr 1.19 or newer sidecar with a binary store component. Payloads must outlive the
    /// workflows that reference them, so use a durable store in production: <c>binarystore.in-memory</c>
    /// loses them when the sidecar restarts, and a workflow that then replays cannot resolve them.
    /// Register it on every app that hosts or schedules these workflows.
    /// </para>
    /// </remarks>
    public static IAgentsBuilder WithBinaryStorePayloads(
        this IAgentsBuilder builder,
        string storeName,
        Action<BinaryStorePayloadOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        var workflowBuilder = DaprAgentsBuilder.From(builder, "expose its Dapr Workflow builder").WorkflowBuilder;

        Dapr.Workflow.BinaryStorePayloadServiceCollectionExtensions.WithBinaryStorePayloads(workflowBuilder, options =>
        {
            options.StoreName = storeName;
            options.WritePreference = PayloadWritePreference.PreferReference;
            configure?.Invoke(options);
        });

        return builder;
    }
}
