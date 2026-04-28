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
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace ResearcherDemo;

/// <summary>
/// Orchestrates a three-agent content pipeline where each agent sees the full conversation history
/// from all prior agents. This means:
/// - The ResearcherAgent sees only its own prompt
/// - The WriterAgent sees the Researcher's prompt, tool calls, reasoning, and output
/// - The EditorAgent sees everything from both the Researcher and the Writer
/// This shared history approach is useful when agents need to understand the full context of how earlier decisions
/// were made (e.g., the editor can see what sources the researcher found and how the writer interpreted them). 
/// </summary>
public sealed partial class ContentPipelineWorkflow : Workflow<PipelineInput, string>
{
	public override async Task<string> RunAsync(WorkflowContext context, PipelineInput input)
	{
		var logger = context.CreateReplaySafeLogger<ContentPipelineWorkflow>();
		
		// Shared conversation log: each agent sees all prior turns
		var conversationLog = new List<WorkflowChatMessage>();
		
		// Step 1: Research the topic
		LogStartingResearch(logger, input.Topic);
		var researcher = context.GetAgent(Constants.ResearchAgent);
		var researchResult = await context.RunAgentWithHistoryAsync(
			researcher,
			message: $"Research the following topic thoroughly. Identify key facts, different perspectives, and any recent developments:\n\n{input.Topic}",
			priorMessages: conversationLog);

		conversationLog.AddRange(researchResult.TurnMessages);
		LogResearchComplete(logger, conversationLog.Count);
		
		// Step 2: Write an article based on that research
		// The writer sess the full research conversation (including any tool calls the researcher made),
		// giving it full context
		LogStartingWriting(logger);
		var writer = context.GetAgent(Constants.WriterAgent);
		var writeResult = await context.RunAgentWithHistoryAsync(
			writer,
			message:
			"Based on the research above, write a well-structured, engaging article. Reference specific findings from the research phase.",
			priorMessages: conversationLog);

		conversationLog.AddRange(writeResult.TurnMessages);
		LogWritingComplete(logger, conversationLog.Count);
		
		// Step 3: Edit and polish the article
		// The editor sees everything: the research and the writing. This allows it to verify
		// claims against the original research and improve the writing.
		LogStartingEditing(logger);
		var editor = context.GetAgent(Constants.EditorAgent);
		var editResult = await context.RunAgentWithHistoryAsync(
			editor,
			message: "Review and improve the article above. Check that claims match the original research," +
			         "improve clarity and flow, and fix any issues. Return the final polished article.",
			priorMessages: conversationLog);
		
		LogEditingComplete(logger);
		return editResult.Response.Text;
	}

	[LoggerMessage(LogLevel.Information, "Starting research phase for topic: '{Topic}'")]
	private static partial void LogStartingResearch(ILogger logger, string topic);

	[LoggerMessage(LogLevel.Information, "Research phase complete. Conversation log has {Count} messages.")]
	private static partial void LogResearchComplete(ILogger logger, int count);

	[LoggerMessage(LogLevel.Information, "Starting writing phase")]
	private static partial void LogStartingWriting(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Writing phase complete. Conversation log has {Count} messages.")]
	private static partial void LogWritingComplete(ILogger logger, int count);

	[LoggerMessage(LogLevel.Information, "Starting editing phase")]
	private static partial void LogStartingEditing(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Editing phase complete")]
	private static partial void LogEditingComplete(ILogger logger);
}

/// <summary>
/// Input for the content pipeline workflow.
/// </summary>
/// <param name="Topic"></param>
public readonly record struct PipelineInput([property: JsonPropertyName("topic")] string Topic);