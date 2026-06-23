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
using Dapr.AI.Conversation.Extensions;
using Dapr.Metadata.Extensions;
using Dapr.StateManagement.Extensions;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

/// <summary>
/// DI extensions for registering agents and the Dapr Workflow runtime.
/// </summary>
public static class DaprAgentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared plumbing for configuring Dapr agents and Dapr Workflow plumbing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureSerialization">Optional callback to configure Dapr Workflow serialization and source-generated <see cref="JsonSerializerContext"/> instances.</param>
    /// <param name="registrations">Used to register workflows and workflow activities from the calling application.</param>
    public static IAgentsBuilder AddDaprAgents(
        this IServiceCollection services,
        Action<DaprAgentsSerializationOptions>? configureSerialization = null,
        Action<WorkflowRuntimeOptions>? registrations = null)
    {
        // Always register the Dapr Conversation client infrastructure. Per-agent keyed
        // DaprChatClient registrations (via conversationComponentName) depend on this
        // shared HTTP/gRPC plumbing, so it must be present regardless of which
        // WithAgent overload the caller uses.
        services.AddDaprConversationClient();
        services.AddDaprStateManagementClient();
        services.AddDaprMetadata();
        
        // Registries + ambient context accessor
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IDaprAgentInvoker, DaprAgentInvoker>();
        services.AddSingleton<IDaprAgentContextAccessor, DaprAgentContextAccessor>();
        services.AddSingleton<ChatClientRegistry>();
        services.AddSingleton<ToolRegistry>();

        // Optional source-generator serialization contracts and Dapr Workflow serializer configuration
        var serializationOptions = new DaprAgentsSerializationOptions();
        configureSerialization?.Invoke(serializationOptions);

        // Workflow + activities: each LLM call and each tool call is a separate activity
        var workflowBuilder = services.AddDaprWorkflowBuilder(
            opt =>
            {
                // Register additional workflows and workflow activities here
                registrations?.Invoke(opt);
            },
            (_, _) => { });

        switch (serializationOptions.SerializerConfiguration)
        {
            case DaprAgentsSerializationOptions.WorkflowSerializerConfiguration.JsonSerializerOptions:
                workflowBuilder.WithJsonSerializer(serializationOptions.JsonSerializerOptions!);
                break;
            case DaprAgentsSerializationOptions.WorkflowSerializerConfiguration.Serializer:
                workflowBuilder.WithSerializer(serializationOptions.WorkflowSerializer!);
                break;
            case DaprAgentsSerializationOptions.WorkflowSerializerConfiguration.SerializerFactory:
                workflowBuilder.WithSerializer(serializationOptions.WorkflowSerializerFactory!);
                break;
        }

        if (serializationOptions.Contexts.Count > 0)
        {
            services.AddSingleton<IAgentJsonTypeInfoResolver>(_ => new AgentJsonTypeInfoResolver(serializationOptions.Contexts));
            services.AddHostedService<AgentJsonResolverInitializer>();
        }

        return new DaprAgentsBuilder(services);
    }
}
