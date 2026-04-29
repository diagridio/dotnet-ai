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
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using TranslationReviewDemo;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprAgents(registrations: opt =>
	{
		opt.RegisterWorkflow<TranslationPipelineWorkflow>();
	})
	.WithAgent(
		agentName: Constants.TranslatorAgent,
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are an expert translator. Translate text accurately while preserving tone, idiom,
		              and cultural nuances. Return only the translated text with no additional commentary. When
		              revising a prior translation based on feedback, maintain consistency with your earlier word
		              choices where appropriate.
		              """,
		serviceLifetime: ServiceLifetime.Singleton)
	.WithAgent(
		agentName: Constants.ReviewerAgent,
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are a translation quality reviewer. Evaluate translations for accuracy, fluency, and naturalness.
		              Identify specified issues (mistranslations, awkward phrasing, lost nuance) and provide concrete suggestions.
		              If the translation needs improvement, include a corrected version.
		              """,
		serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();
	
// POST /translate — Start the translation pipeline workflow.
// Two agents with isolated histories: the reviewer evaluates without seeing
// the translator's reasoning, and the translator revises using its own prior context.
	app.MapPost("/translate", async (
		DaprWorkflowClient client,
		TranslationInput input) =>
	{
		var id = Guid.NewGuid().ToString("N");
		await client.ScheduleNewWorkflowAsync(nameof(TranslationPipelineWorkflow), id, input);
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
					? state.ReadOutputAs<TranslationOutput?>()
					: null
			})
			: Results.NotFound());
 
await app.RunAsync();
