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
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprConversationClient();

var tools = new List<AITool>
{
	AIFunctionFactory.Create((string city) =>
	{
		Console.WriteLine($">>> TOOL 1: Searching venues in '{city}'...");
		Console.WriteLine(">>> TOOL 1 COMPLETE: Found 3 venues");
		return $"Found 3 venues in {city}. Now call step_two_compare.";
	}, "step_one_search", "Search for event venues in a city. This is the first step."),

	AIFunctionFactory.Create((string data) =>
	{
		Console.WriteLine(">>> TOOL 2: Comparing venues...");
		Console.WriteLine(">>> TOOL 2 COMPLETE: Grand Ballroom is the best option");
		return "Grand Ballroom is the best option. Now call step_three_confirm.";
	}, "step_two_compare", "Compare the venue options. This is the second step."),

	AIFunctionFactory.Create((string selection) =>
	{
		Console.WriteLine(">>> TOOL 3: Confirming booking...");
		Console.WriteLine(">>> TOOL 3 COMPLETE: Booking confirmed for Grand Ballroom");
		return "Booking confirmed for Grand Ballroom. All steps complete!";
	}, "step_three_confirm", "Confirm the venue booking. This is the third and final step."),
};

builder.Services.AddDaprAgents()
	.WithAgent(
		agentName: "EventPlannerAgent",
		conversationComponentName: "conversation-ollama",
		instructions: """
		              You are an event planner. Call all three tools in sequence:
		              1. First call step_one_search with the city name
		              2. Then call step_two_compare with the result from step 1
		              3. Finally call step_three_confirm with the result from step 2
		              Do NOT skip any steps.
		              """,
		serviceLifetime: ServiceLifetime.Singleton,
		tools: tools);

var app = builder.Build();

app.MapPost("/run", async (IDaprAgentInvoker invoker, RunRequest req, CancellationToken ct) =>
{
	var agent = invoker.GetAgent("EventPlannerAgent");
	var result = await invoker.RunAgentAsync(agent, req.Prompt, cancellationToken: ct);
	return Results.Ok(new { response = result.Text });
});

app.Run();

sealed record RunRequest(string Prompt);