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

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprAgents()
	.WithAgent(
		agentName: "ChatAgent",
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are a helpful travel planning assistant. You help users plan trips, suggest
		              destinations, and remember their preferences across the conversation. Always refer
		              back to earlier parts of the conversation when relevant.
		              """,
		serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

// POST /chat — Multi-turn conversation with session continuity.
//
// First request (no sessionId): creates a new session and returns the session ID.
// Subsequent requests (with sessionId): continues the conversation within the same session.
// The session workflow maintains the full conversation history durably — if the process
// crashes mid-conversation, recovery picks up where it left off.
app.MapPost("/chat", async (
	IDaprAgentInvoker invoker,
	DaprWorkflowClient workflowClient,
	ChatRequest req,
	CancellationToken ct) =>
{
	AgentSession session;
	if (req.SessionId is null)
	{
		// First turn: start a new session workflow.
		session = await invoker.CreateSessionAsync(workflowClient, cancellationToken: ct);
	}
	else
	{
		// Subsequent turns: re-attach to the running session workflow.
		session = invoker.AttachSession(req.SessionId);
	}
 
	var agent = invoker.GetAgent("ChatAgent");
	var response = await invoker.RunAgentAsync(agent, req.Message, session, cancellationToken: ct);
 
	return Results.Ok(new
	{
		sessionId = session.GetSessionInstanceId(),
		response = response.Text
	});
});
 
await app.RunAsync();
 
sealed record ChatRequest(string Message, string? SessionId = null);
