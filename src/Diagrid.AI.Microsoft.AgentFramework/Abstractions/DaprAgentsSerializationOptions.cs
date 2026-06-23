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

using System.Text.Json;
using System.Text.Json.Serialization;
using Dapr.Common.Serialization;
using Dapr.Workflow.Serialization;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Serialization options used by <see cref="DaprAgentsServiceCollectionExtensions.AddDaprAgents"/>.
/// </summary>
public sealed class DaprAgentsSerializationOptions
{
    internal List<JsonSerializerContext> Contexts { get; } = [];
    internal JsonSerializerOptions? JsonSerializerOptions { get; private set; }
    internal IDaprSerializer? WorkflowSerializer { get; private set; }
    internal Func<IServiceProvider, IWorkflowSerializer>? WorkflowSerializerFactory { get; private set; }
    internal WorkflowSerializerConfiguration SerializerConfiguration { get; private set; }

    /// <summary>
    /// Configures the JSON serializer options used by Dapr Workflow.
    /// </summary>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use.</param>
    /// <returns>The current options instance.</returns>
    public DaprAgentsSerializationOptions UseJsonSerializerOptions(JsonSerializerOptions jsonSerializerOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonSerializerOptions);
        JsonSerializerOptions = jsonSerializerOptions;
        WorkflowSerializer = null;
        WorkflowSerializerFactory = null;
        SerializerConfiguration = WorkflowSerializerConfiguration.JsonSerializerOptions;
        return this;
    }

    /// <summary>
    /// Configures a custom Dapr Workflow serializer.
    /// </summary>
    /// <param name="serializer">The custom serializer to use.</param>
    /// <returns>The current options instance.</returns>
    public DaprAgentsSerializationOptions UseSerializer(IDaprSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        JsonSerializerOptions = null;
        WorkflowSerializer = serializer;
        WorkflowSerializerFactory = null;
        SerializerConfiguration = WorkflowSerializerConfiguration.Serializer;
        return this;
    }

    /// <summary>
    /// Configures a custom Dapr Workflow serializer using a factory.
    /// </summary>
    /// <param name="serializerFactory">A factory that creates the serializer using the service provider.</param>
    /// <returns>The current options instance.</returns>
    public DaprAgentsSerializationOptions UseSerializer(Func<IServiceProvider, IWorkflowSerializer> serializerFactory)
    {
        ArgumentNullException.ThrowIfNull(serializerFactory);
        JsonSerializerOptions = null;
        WorkflowSerializer = null;
        WorkflowSerializerFactory = serializerFactory;
        SerializerConfiguration = WorkflowSerializerConfiguration.SerializerFactory;
        return this;
    }

    /// <summary>
    /// Adds a source-generated <see cref="JsonSerializerContext"/> instance.
    /// </summary>
    /// <param name="context">The context instance, e.g. <c>ExampleJsonContext.Default</c>.</param>.
    public DaprAgentsSerializationOptions AddContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Contexts.Add(context);
        return this;
    }

    /// <summary>
    /// Adds a source-generated <see cref="JsonSerializerContext"/> using a factory.
    /// </summary>
    /// <param name="factory">A factory that returns the context instance, typically <c>() => ExampleJsonContext.Default</c>.</param>
    /// <typeparam name="TContext"></typeparam>
    /// <returns></returns>
    public DaprAgentsSerializationOptions AddContext<TContext>(Func<TContext> factory)
        where TContext : JsonSerializerContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        var context = factory();
        Contexts.Add(context);
        return this;
    }

    internal enum WorkflowSerializerConfiguration
    {
        None,
        JsonSerializerOptions,
        Serializer,
        SerializerFactory,
    }
}
