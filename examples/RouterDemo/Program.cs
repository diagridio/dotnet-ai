// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Dapr.AI.Conversation.Extensions;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using RouterDemo.Agents;
using RouterDemo.Models;
using RouterDemo.Workflows;

var builder = WebApplication.CreateBuilder(args);

var catalog = new AgentCatalog();

builder.Services.AddSingleton(catalog);
builder.Services.AddDaprConversationClient();

var agentsBuilder = builder.Services.AddDaprAgents(
    opt => opt.AddContext(() => AgentRouterJsonContext.Default),
    opt =>
    {
        opt.RegisterWorkflow<AgentRouterWorkflow>();
        opt.RegisterActivity<RouteWithAgentActivity>();
    });

agentsBuilder.WithAgent(sp => RouterWorkflowAgentFactory.Create(sp, catalog.RouterWorkflow));

agentsBuilder
    .WithAgent(
        catalog.Router.Name,
        catalog.Router.ConversationComponentName,
        catalog.Router.Prompt,
        serviceLifetime: ServiceLifetime.Singleton)
    .WithAgent(
        catalog.Coordinator.Name,
        catalog.Coordinator.ConversationComponentName,
        catalog.Coordinator.Prompt,
        serviceLifetime: ServiceLifetime.Singleton);

foreach (var agent in catalog.RoutableAgents)
{
    agentsBuilder.WithAgent(
        agent.Name,
        agent.ConversationComponentName,
        agent.Prompt,
        serviceLifetime: ServiceLifetime.Singleton);
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { message = "Agent router demo is running." }));

app.MapGet("/agents", (AgentRegistry registry, AgentCatalog agents) =>
{
    var available = agents.GetRoutableAgents(registry)
        .Select(agent => new
        {
            agent.Name,
            agent.Purpose,
            agent.OutputSchema,
            agent.ConversationComponentName
        });

    return Results.Ok(available);
});

app.MapPost("/route", async (
    IDaprAgentInvoker invoker,
    AgentRegistry registry,
    AgentCatalog agents,
    RouteRequest request,
    CancellationToken ct) =>
{
    var available = agents.GetRoutableAgents(registry);
    if (available.Count == 0)
    {
        return Results.BadRequest(new { error = "No routable agents are currently registered." });
    }

    var result = await invoker.RunAgentAndDeserializeAsync<AgentRouterWorkflow.RoutingResult, Program>(
        agentName: agents.RouterWorkflow.Name,
        message: request.Input,
        cancellationToken: ct);

    return string.IsNullOrWhiteSpace(result.Status)
        ? Results.NoContent()
        : Results.Ok(result);
});

await app.RunAsync();
