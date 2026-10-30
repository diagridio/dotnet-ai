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

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Builds what one LLM call of <see cref="CallLlmActivity"/> sends (its messages and its <see cref="ChatOptions"/>)
/// and reads back what the model returned. Pure: the activity does the lookups, the call and the logging.
/// </summary>
internal static class LlmRequestBuilder
{
    /// <summary>
    /// The messages of the call, and where each part starts: the combined instructions and the context providers'
    /// messages (the head), then one chat message per entry of <paramref name="messages"/>, of which the first
    /// <paramref name="runMessagesStart"/> are earlier turns.
    /// </summary>
    internal static PromptLayout BuildChatMessages(
        string? instructions,
        string? additionalInstructions,
        List<WorkflowChatMessage>? additionalMessages,
        List<WorkflowChatMessage> messages,
        string reasoningSource,
        ReasoningReplay replay,
        int runMessagesStart,
        bool historyStartsWithSummary)
    {
        var chatMessages = new List<ChatMessage>();

        var combinedInstructions = CombineInstructions(instructions, additionalInstructions);
        if (!string.IsNullOrWhiteSpace(combinedInstructions))
        {
            chatMessages.Add(new ChatMessage(ChatRole.System, combinedInstructions));
        }

        // Ephemeral context contributed by AIContextProviders for this run only (e.g. retrieved
        // memories). Never persisted to TurnMessages/session history — see ResolveAgentContextActivity.
        if (additionalMessages is { Count: > 0 })
        {
            foreach (var msg in additionalMessages)
            {
                chatMessages.Add(WorkflowChatMessageConverter.ToChatMessage(msg, reasoningSource));
            }
        }

        // Reasoning this agent produced earlier goes back to the model: from prior turns too with
        // AcrossTurns, from this run only with WithinRun, and never with Off (ReasoningReplayPolicy).
        for (var i = 0; i < messages.Count; i++)
        {
            var sendReasoning = ReasoningReplayPolicy.SendsReasoning(replay, i, runMessagesStart);
            chatMessages.Add(WorkflowChatMessageConverter.ToChatMessage(messages[i], sendReasoning ? reasoningSource : null));
        }

        return new PromptLayout(chatMessages, chatMessages.Count - messages.Count,
            Math.Min(runMessagesStart, messages.Count), historyStartsWithSummary);
    }

    internal static string? CombineInstructions(string? instructions, string? additionalInstructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return additionalInstructions;
        }

        if (string.IsNullOrWhiteSpace(additionalInstructions))
        {
            return instructions;
        }

        return $"{instructions}\n\n{additionalInstructions}";
    }

    /// <summary>
    /// Builds the options for one LLM call: the agent's own <see cref="ChatOptions"/> (temperature,
    /// output limits, ...), the resolved reasoning effort, the run's options where it sets them, and
    /// the agent's tools plus those its context providers contributed.
    /// </summary>
    internal static ChatOptions? BuildChatOptions(
        ChatOptions? baseOptions,
        IList<AITool>? tools,
        IReadOnlyList<AITool>? additionalTools,
        AgentRunOptions? agentRunOptions,
        ReasoningOptions? reasoning,
        bool isFollowUpCall)
    {
        var combinedTools = CombineTools(tools, additionalTools);
        if (baseOptions is null && combinedTools is null && agentRunOptions is null && reasoning is null)
        {
            return null;
        }

        // Cloned per call: the registered options are shared by every call of the agent.
        var options = baseOptions?.Clone() ?? new ChatOptions();
        options.Reasoning = reasoning;
        options.Tools = combinedTools;

        if (isFollowUpCall && options.ToolMode is RequiredChatToolMode)
        {
            options.ToolMode = null;
        }

        if (agentRunOptions is not null)
        {
#pragma warning disable MEAI001
            // The run overrides only what it sets, so an agent's own ResponseFormat is not erased.
            options.AllowBackgroundResponses = agentRunOptions.AllowBackgroundResponses ?? options.AllowBackgroundResponses;
            options.ContinuationToken = agentRunOptions.ContinuationToken ?? options.ContinuationToken;
#pragma warning restore MEAI001
            options.ResponseFormat = agentRunOptions.ResponseFormat ?? options.ResponseFormat;
            options.AdditionalProperties = MergeAdditionalProperties(options.AdditionalProperties, agentRunOptions.AdditionalProperties);
        }

        return options;
    }

    private static AdditionalPropertiesDictionary? MergeAdditionalProperties(
        AdditionalPropertiesDictionary? agentProperties,
        AdditionalPropertiesDictionary? runProperties)
    {
        if (agentProperties is not { Count: > 0 })
        {
            return runProperties;
        }

        if (runProperties is not { Count: > 0 })
        {
            return agentProperties;
        }

        var merged = new AdditionalPropertiesDictionary(agentProperties);
        foreach (var (key, value) in runProperties)
        {
            merged[key] = value;
        }

        return merged;
    }

    internal static List<AITool>? CombineTools(IList<AITool>? tools, IReadOnlyList<AITool>? additionalTools)
    {
        if (tools is not { Count: > 0 } && additionalTools is not { Count: > 0 })
        {
            return null;
        }

        var combined = new List<AITool>(
            (tools?.Count ?? 0) + (additionalTools?.Count ?? 0));

        if (tools is { Count: > 0 })
        {
            combined.AddRange(tools);
        }

        if (additionalTools is { Count: > 0 })
        {
            combined.AddRange(additionalTools);
        }

        return combined;
    }

    internal static CallLlmOutput ParseResponse(ChatResponse response, string reasoningSource)
    {
        if (response.Messages.Count == 0)
        {
            throw new InvalidOperationException("The LLM returned a response with no messages.");
        }

        var responseMessage = response.Messages[^1];
        var functionCalls = responseMessage.Contents.OfType<FunctionCallContent>().ToList();
        var text = responseMessage.Text;

        var isFinal = functionCalls.Count == 0;

        return new CallLlmOutput
        {
            IsFinal = isFinal,
            Text = text,
            // Some clients return reasoning as a message of its own, so read it from every message.
            Reasoning = WorkflowChatMessageConverter.ToWorkflowReasoning(
                response.Messages.SelectMany(m => m.Contents), reasoningSource),
            FunctionCalls = isFinal
                ? null
                : functionCalls.Select(fc => new WorkflowFunctionCall
                {
                    CallId = fc.CallId ?? Guid.NewGuid().ToString("N"),
                    Name = fc.Name,
                    ArgumentsJson = fc.Arguments is { Count: > 0 }
                        ? JsonSerializer.Serialize(fc.Arguments)
                        : "{}"
                }).ToList()
        };
    }
}
