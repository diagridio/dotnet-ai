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

namespace TranslationReviewDemo;

public sealed partial class TranslationPipelineWorkflow : Workflow<TranslationInput, TranslationOutput>
{
	public override async Task<TranslationOutput> RunAsync(WorkflowContext context, TranslationInput input)
	{
		var logger = context.CreateReplaySafeLogger<TranslationPipelineWorkflow>();
		
		// Each agent maintains its own isolated conversation history.
        var translatorLog = new List<WorkflowChatMessage>();
        var reviewerLog = new List<WorkflowChatMessage>();
 
        // Step 1: Translate the text.
        LogStartTranslation(logger, input.TargetLanguage);
        var translator = context.GetAgent("TranslatorAgent");
        var translationResult = await context.RunAgentWithHistoryAsync(
            translator,
            message: $"Translate the following text to {input.TargetLanguage}. " +
                     $"Return only the translated text, no commentary.\n\n{input.Text}",
            priorMessages: translatorLog);
 
        translatorLog.AddRange(translationResult.TurnMessages);
        var translatedText = translationResult.Response.Text ?? string.Empty;
        LogEndTranslation(logger);
 
        // Step 2: Review the translation independently.
        // The reviewer gets ONLY the original + translation, not the translator's
        // internal reasoning. This prevents bias. The reviewer evaluates the
        // translation on its own merits.
        LogStartReview(logger);
        var reviewer = context.GetAgent("ReviewerAgent");
        var reviewResult = await context.RunAgentWithHistoryAsync(
            reviewer,
            message: $"Review this translation for accuracy, fluency, and naturalness.\n\n" +
                     $"Original ({input.SourceLanguage}):\n{input.Text}\n\n" +
                     $"Translation ({input.TargetLanguage}):\n{translatedText}\n\n" +
                     "List any issues found and suggest improvements. " +
                     "If improvements are needed, provide the corrected translation.",
            priorMessages: reviewerLog);
 
        reviewerLog.AddRange(reviewResult.TurnMessages);
        var reviewText = reviewResult.Response.Text ?? string.Empty;
        LogEndReview(logger);
 
        // Step 3: If the reviewer suggested improvements, ask the translator to revise.
        // The translator sees its OWN prior translation (from translatorLog) and the
        // review feedback, so it can produce a consistent revision.
        string finalText;
        if (reviewText.Contains("corrected", StringComparison.OrdinalIgnoreCase) ||
            reviewText.Contains("improvement", StringComparison.OrdinalIgnoreCase) ||
            reviewText.Contains("issue", StringComparison.OrdinalIgnoreCase))
        {
            LogFoundIssues(logger);
            var revisionResult = await context.RunAgentWithHistoryAsync(
                translator,
                message: $"A reviewer provided the following feedback on your translation. " +
                         $"Please revise accordingly and return only the improved translation.\n\n" +
                         $"Feedback:\n{reviewText}",
                priorMessages: translatorLog);
 
            translatorLog.AddRange(revisionResult.TurnMessages);
            finalText = revisionResult.Response.Text ?? translatedText;
            LogRevisionComplete(logger);
        }
        else
        {
            finalText = translatedText;
            LogNoIssues(logger);
        }
 
        return new TranslationOutput(
            input.SourceLanguage,
            input.TargetLanguage,
            input.Text,
            finalText,
            reviewText);
	}

	[LoggerMessage(LogLevel.Information, "Translating text to '{Language}'")]
	private static partial void LogStartTranslation(ILogger logger, string language);

	[LoggerMessage(LogLevel.Information, "Translation complete")]
	private static partial void LogEndTranslation(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Reviewing translation quality")]
	private static partial void LogStartReview(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Review complete")]
	private static partial void LogEndReview(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Reviewer found issues, requesting revision")]
	private static partial void LogFoundIssues(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Revision complete")]
	private static partial void LogRevisionComplete(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Reviewer approved translation, no revision needed")]
	private static partial void LogNoIssues(ILogger logger);
}

public readonly record struct TranslationInput(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("sourceLanguage")] string SourceLanguage,
	[property: JsonPropertyName("targetLanguage")] string TargetLanguage);
	
public readonly record struct TranslationOutput(
	[property: JsonPropertyName("sourceLanguage")] string SourceLanguage,
	[property: JsonPropertyName("targetLanguage")] string TargetLanguage,
	[property: JsonPropertyName("originalText")] string OriginalText,
	[property: JsonPropertyName("translatedText")] string TranslatedText,
	[property: JsonPropertyName("reviewNotes")] string ReviewNotes);