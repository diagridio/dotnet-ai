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

using Dapr.AI.Conversation.Extensions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Dapr.AI.Microsoft.Extensions;
using Dapr.Workflow;
using EmailGateDemo.Models;
using EmailGateDemo.Workflows;

var builder = WebApplication.CreateBuilder(args);

// Register the Dapr-backed IChatClient
builder.Services.AddDaprConversationClient();
builder.Services.AddDaprChatClient("conversation-ollama", ServiceLifetime.Singleton);

// Create agents to use with the Microsoft Agent Framework
builder.Services.AddDaprAgents(opt =>
{
    opt.AddContext(() => ExpectedJsonContent.Default); // Expected [JsonSerializable] context
}, opt =>
{
    opt.RegisterWorkflow<SpamGateAndReplyWorkflow>();
}).WithAgent(
    agentName: "EmailAssistant",
    conversationComponentName: "conversation-ollama",
    instructions: "Write concise, processional emails.",
    serviceLifetime: ServiceLifetime.Singleton)
 .WithAgent(
    agentName: "SpamDetectionAgent",
    conversationComponentName: "conversation-ollama",
    instructions: "Detect spam and explain why.",
    serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

app.MapPost("/draft", async (DaprWorkflowClient client, SpamGateAndReplyWorkflow.EmailInput input) =>
{
    var id = Guid.NewGuid().ToString("N");
    await client.ScheduleNewWorkflowAsync(nameof(SpamGateAndReplyWorkflow), id, input);
    return Results.Accepted($"/status/{id}", new { instanceId = id });
});

app.MapGet("/status/{id}",
    async (DaprWorkflowClient client, string id, CancellationToken ct) =>
        await client.GetWorkflowStateAsync(id, cancellation: ct) is { } state ? Results.Ok(state) : Results.NotFound());

await app.RunAsync();
