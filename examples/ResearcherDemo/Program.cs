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
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using ResearcherDemo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprConversationClient();

builder.Services.AddDaprAgents(registrations: opt =>
	{
		opt.RegisterWorkflow<ContentPipelineWorkflow>();
	})
	.WithAgent(
		agentName: Constants.ResearchAgent,
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are a thorough research assistant. When given a topic, identify the most important
		              facts, perspectives, and recent developments. Organize your findings clearly with specific
		              details and sources when possible.
		              """,
		serviceLifetime: ServiceLifetime.Singleton)
	.WithAgent(
		agentName: Constants.WriterAgent,
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are a skilled article writer. You take research findings and transform them into
		              engaging, well-structured articles. Use clear paragraphs, strong transitions,
		              and reference specific findings from the research you've been given.)
		              """,
		serviceLifetime: ServiceLifetime.Singleton)
	.WithAgent(
		agentName: Constants.EditorAgent,
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are a meticulous editor. Review articles for accuracy, clarity, and flow. Cross-reference
		              claims against the original research. Fix grammar, improve sentence structure, and ensure the 
		              article is polished and ready to publish. Return only the final edited article.
		              """,
		serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();

// POST /pipeline — Start the content pipeline workflow.
// Three agents collaborate with a shared conversation history:
// Researcher → Writer → Editor, each seeing all prior context.
app.MapPost("/pipeline", async (DaprWorkflowClient client, PipelineInput input) =>
{
	var id = Guid.NewGuid().ToString("N");
	await client.ScheduleNewWorkflowAsync(nameof(ContentPipelineWorkflow), id, input);
	return Results.Accepted($"/status/{id}", new { instanceId = id });
});
 
// GET /status/{id} — Check the workflow status and final result.
app.MapGet("/status/{id}",
	async (DaprWorkflowClient client, string id, CancellationToken ct) =>
		await client.GetWorkflowStateAsync(id, cancellation: ct) is { } state
			? Results.Ok(new
			{
				status = state.RuntimeStatus.ToString(),
				result = state.RuntimeStatus == WorkflowRuntimeStatus.Completed
					? state.ReadOutputAs<string>()
					: null
			})
			: Results.NotFound());
 
await app.RunAsync();
