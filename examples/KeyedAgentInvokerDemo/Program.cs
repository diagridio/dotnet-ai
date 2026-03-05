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
using KeyedAgentInvokerDemo.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprConversationClient();

const string OllamaComponentName = "conversation-ollama";
const string OpenAiComponentName = "conversation-openai";
const string OllamaAgentName = "KeyedAgent.Ollama";
const string OpenAiAgentName = "KeyedAgent.OpenAI";

var agentNamesByComponent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [OllamaComponentName] = OllamaAgentName,
    [OpenAiComponentName] = OpenAiAgentName
};

// Register distinct agent names per conversation component to avoid overwrites
builder.Services.AddDaprAgents()
    .WithAgent(
        OllamaAgentName,
        OllamaComponentName,
        "You are a helpful assistant. Keep answers concise.",
        serviceLifetime: ServiceLifetime.Singleton)
    .WithAgent(
        OpenAiAgentName,
        OpenAiComponentName,
        "You are a helpful assistant. Answer in JSON with fields 'answer' and 'confidence'.",
        serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

app.MapPost("/ask/{componentName}", async (
    IDaprAgentInvoker invoker,
    string componentName,
    AskRequest request,
    CancellationToken ct) =>
{
    if (!agentNamesByComponent.TryGetValue(componentName, out var agentName))
    {
        return Results.BadRequest(new
        {
            error = $"Unknown component name '{componentName}'. Valid names: {string.Join(", ", agentNamesByComponent.Keys)}"
        });
    }

    var agent = invoker.GetAgent(agentName, componentName);
    var response = await invoker.RunAgentAsync(agent, request.Prompt, cancellationToken: ct);
    return Results.Ok(new { response = response.Text });
});

await app.RunAsync();
