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
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Dapr.Workflow;

namespace EmailGateDemo.Workflows;

/// <summary>
/// Orchestrates a two-step flow.
/// 1) Run the spam detection agent to decide whether the message is spam.
/// 2) If not spam, run the email assistant agent to produce a short, professional reply.
/// </summary>
/// <remarks>
/// This workflow remains deterministic: all non-deterministic work (LLM calls, I/O) is executed
/// inside activities by <see cref="InvokeAgentActivity"/>. The workflow only performs JSON parsing
/// and branching.
/// </remarks>
public sealed partial class SpamGateAndReplyWorkflow : Workflow<SpamGateAndReplyWorkflow.EmailInput, string>
{
    /// <inheritdoc />
    public override async Task<string> RunAsync(WorkflowContext context, EmailInput input)
    {
        var logger = context.CreateReplaySafeLogger<SpamGateAndReplyWorkflow>();
        
        // Gate on whether the input is spam
        var spamAgent = context.GetAgent("SpamDetectionAgent");
        
        // Ask the spam agent for a structured JSON result (isSpam + reason).
        // The generic helper uses source-generated metadata from registered JsonSerializerContext(s).
        var spam = await context.RunAgentAndDeserializeAsync<SpamResult>(
                agent: spamAgent,
                message: $"Analyze and return JSON: {{\"isSpam\": bool, \"reason\": string}}. \n\nEmail:\n{input.Body}",
                logger: logger)
            .ConfigureAwait(false);

        if (spam.IsSpam)
        {
            LogSpamDetected(logger, spam.Reason);
            return $"SPAM: {spam.Reason}";
        }

        // Draft a response
        var emailAgent = context.GetAgent("EmailAssistant");
        var reply = await context.RunAgentAndDeserializeAsync<DraftReply>(
            agent: emailAgent,
            message: $"Draft a short, professional reply to:\n\n{input.Body}",
            logger: logger)
            .ConfigureAwait(false);

        return reply.Response ?? "(no reply)";
    }

    [LoggerMessage(LogLevel.Information, "Spam was detected from the agent with the reason: '{Reason}'")]
    private static partial void LogSpamDetected(ILogger logger, string reason);
    
    /// <summary>
    /// Input payload for <see cref="SpamGateAndReplyWorkflow"/>.
    /// </summary>
    /// <param name="Body">The raw email text supplied to the workflow.</param>
    public readonly record struct EmailInput(
        [property: JsonPropertyName("body")] string Body);
    
    /// <summary>
    /// Structured result returned by the spam detection agent.
    /// </summary>
    /// <param name="IsSpam">True if the input is considered spam.</param>
    /// <param name="Reason">Short explanation to support the decision.</param>
    public readonly record struct SpamResult(
        [property: JsonPropertyName("isSpam")] bool IsSpam, 
        [property: JsonPropertyName("reason")] string Reason);
    
    /// <summary>
    /// Structured result returned by the email assistant agent.
    /// </summary>
    /// <param name="Response">The drafted email reply text.</param>
    public readonly record struct DraftReply(
        [property: JsonPropertyName("response")] string Response);
}
