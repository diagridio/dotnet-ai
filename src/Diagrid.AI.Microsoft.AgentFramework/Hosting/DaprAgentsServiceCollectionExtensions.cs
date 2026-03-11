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
    /// <param name="configureSerialization">Optional callback to register source-generated <see cref="JsonSerializerContext"/> instances.</param>
    /// <param name="registrations">Used to register workflows and workflow activities from the calling application.</param>
    public static IAgentsBuilder AddDaprAgents(
        this IServiceCollection services,
        Action<DaprAgentsSerializationOptions>? configureSerialization = null,
        Action<WorkflowRuntimeOptions>? registrations = null)
    {
        // Registry + ambient context accessor
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IDaprAgentInvoker, DaprAgentInvoker>();
        services.AddSingleton<IDaprAgentContextAccessor, DaprAgentContextAccessor>();
        
        // Activity + minimal wrapper workflow
        services.AddDaprWorkflow(opt =>
        {
            opt.RegisterWorkflow<AgentRunWorkflow>();
            opt.RegisterActivity<InvokeAgentActivity>();

            // Register additional workflows and workflow activities here
            registrations?.Invoke(opt);
        });
        
        // Optional source-generator serialization contracts
        var serializationOptions = new DaprAgentsSerializationOptions();
        configureSerialization?.Invoke(serializationOptions);
        if (serializationOptions.Contexts.Count > 0)
        {
            services.AddSingleton<IAgentJsonTypeInfoResolver>(_ => new AgentJsonTypeInfoResolver(serializationOptions.Contexts));
            services.AddHostedService<AgentJsonResolverInitializer>();
        }

        return new DaprAgentsBuilder(services);
    }
}
