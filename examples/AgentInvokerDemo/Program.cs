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

using AgentInvokerDemo.Models;
using Dapr.AI.Conversation.Extensions;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprConversationClient();
builder.Services.AddDaprAgents(opt =>
{
    opt.AddContext(() => AgentInvokerJsonContext.Default);
}).WithAgent(
    agentName: "InvokerAgent",
    conversationComponentName: "conversation-ollama",
    instructions: "You are a helpful assistant. Answer normally unless the prompt asks for JSON.",
    serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

app.MapPost("/ask", async (IDaprAgentInvoker invoker, AskRequest request, CancellationToken ct) =>
{
    var agent = invoker.GetAgent("InvokerAgent");
    var response = await invoker.RunAgentAsync(agent, request.Prompt, cancellationToken: ct);
    return Results.Ok(new { response = response.Text });
});

app.MapPost("/ask-typed", async (IDaprAgentInvoker invoker, AskRequest request, CancellationToken ct) =>
{
    var agent = invoker.GetAgent("InvokerAgent");
    var message =
        $"{request.Prompt}{Environment.NewLine}{Environment.NewLine}" +
        "Return JSON only with schema: {\"answer\":\"string\",\"confidence\":0.0}";

    var result = await invoker.RunAgentAndDeserializeAsync<StructuredAnswer, Program>(
        agent,
        message: message,
        cancellationToken: ct);

    return result is null ? Results.NoContent() : Results.Ok(result);
});

await app.RunAsync();
