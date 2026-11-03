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
using Dapr.AI.Conversation;
using Dapr.AI.Conversation.Extensions;
using Dapr.Metadata.Extensions;
using Dapr.StateManagement.Extensions;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // One anonymous usage event per process, never blocking and never throwing. See
        // UsageAnalytics and the README "Usage analytics" section.
        UsageAnalytics.Report(
            "Diagrid.AI.Microsoft.AgentFramework",
            new Dictionary<string, string?>
            {
                ["kind"] = "agent",
                ["framework"] = "MicrosoftAgentFramework",
                ["framework_version"] = UsageAnalytics.FrameworkVersion(),
            });

        // Always register the Dapr Conversation client infrastructure. Per-agent keyed
        // DaprChatClient registrations (via conversationComponentName) depend on this
        // shared HTTP/gRPC plumbing, so it must be present regardless of which
        // WithAgent overload the caller uses.
        //
        // AddDaprConversationClient appends a descriptor rather than TryAdd-ing one, so calling it
        // unconditionally would silently override a client the host registered first (e.g. with
        // UseTimeout). Ours lifts the 100-second HttpClient.Timeout default to the maximum, because
        // CallLlmActivity applies the per-agent timeout from WithLlmTimeout(...) to each call.
        // Only an unkeyed registration counts: DaprChatClient resolves the unkeyed client, so a
        // keyed one the host registered for its own use cannot stand in for it.
        if (!services.Any(d => d.ServiceType == typeof(DaprConversationClient) && !d.IsKeyedService))
        {
            services.AddDaprConversationClient((_, clientBuilder) =>
                clientBuilder.UseTimeout(LlmTimeoutRegistration.MaxTimeout));
        }
        services.AddDaprStateManagementClient();
        services.AddDaprMetadata();
        
        // Registries + ambient context accessor
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IDaprAgentInvoker, DaprAgentInvoker>();
        services.AddSingleton<IDaprAgentContextAccessor, DaprAgentContextAccessor>();
        services.AddSingleton<ChatClientRegistry>();
        services.AddSingleton<ToolRegistry>();

        // Safe-by-default approval gate for tools that require human approval (e.g. an MAF skill
        // script when AgentSkillsProviderOptions.ScriptApproval is enabled) — denies until a host
        // registers its own IToolApprovalHandler (register it BEFORE calling AddDaprAgents()).
        services.TryAddSingleton<IToolApprovalHandler, DenyingToolApprovalHandler>();

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

        return new DaprAgentsBuilder(services, workflowBuilder);
    }
}
