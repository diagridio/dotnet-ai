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
using Dapr.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that performs a single LLM call. Each invocation is checkpointed by Dapr Workflows,
/// so on crash recovery the result is replayed without re-executing the LLM call.
/// </summary>
internal sealed partial class CallLlmActivity(
    ChatClientRegistry chatClientRegistry,
    AgentRegistry agentRegistry,
    ToolRegistry toolRegistry,
    IServiceProvider serviceProvider,
    ILogger<CallLlmActivity> logger) : WorkflowActivity<CallLlmInput, CallLlmOutput>
{
    /// <inheritdoc />
    public override async Task<CallLlmOutput> RunAsync(WorkflowActivityContext context, CallLlmInput input)
    {
        AgentTelemetryBaggage.SetAgent(
            input.AgentName,
            input.ChatClientKey,
            AgentTelemetryBaggage.LlmOperation,
            input.TelemetryBaggage);

        var config = chatClientRegistry.Get(input.AgentName);
        if (config is null)
        {
            // Trigger lazy agent resolution — this runs the factory which
            // populates ChatClientRegistry as a side effect for ChatClientAgent types.
            agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
            config = chatClientRegistry.Get(input.AgentName)
                ?? throw new InvalidOperationException(
                    $"Agent '{input.AgentName}' did not register a chat client configuration. " +
                    "Ensure the agent is a ChatClientAgent created via AsAIAgent() or registered " +
                    "with explicit instructions and tools via the DaprAgentsBuilderExtensions overloads.");
        }

        LogLlmCallInfo(input.AgentName, input.Messages.Count);

        try
        {
            // Drive the AIContextProvider pipeline (e.g. AgentSkillsProvider). Because this
            // library bypasses MAF's agent-run pipeline (it calls the raw IChatClient directly),
            // context providers would never fire otherwise. We invoke them here per LLM call and
            // merge their contributed instructions/messages/tools into the request.
            var contribution = await InvokeContextProvidersAsync(config, input, CancellationToken.None)
                .ConfigureAwait(false);

            var messages = BuildChatMessages(config.Instructions, contribution, input.Messages);
            var options = BuildChatOptions(config.Tools, contribution, input.Options);

            var response = await config.ChatClient.GetResponseAsync(messages, options)
                .ConfigureAwait(false);

            if (contribution is not null)
            {
                // Best-effort post-invocation notification; never fail the turn over it.
                await NotifyProvidersInvokedAsync(contribution, messages, response, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return ParseResponse(response);
        }
        catch (Exception ex)
        {
            LogLlmCallError(input.AgentName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Invokes each registered <see cref="AIContextProvider"/> for the agent and collects the
    /// instructions, messages, and tools they contribute for this turn. Any contributed invokable
    /// tool is also registered in the <see cref="ToolRegistry"/> so that a subsequent
    /// <see cref="ExecuteToolActivity"/> (a separate, possibly replayed activity) can resolve and
    /// invoke it — for example the <c>load_skill</c> / <c>read_skill_resource</c> tools contributed
    /// by an <c>AgentSkillsProvider</c>.
    /// </summary>
    private async Task<ContextProviderPipeline.Contribution?> InvokeContextProvidersAsync(
        ChatClientRegistry.AgentChatConfig config,
        CallLlmInput input,
        CancellationToken cancellationToken)
    {
        if (config.ContextProviders is not { Count: > 0 } providers)
        {
            return null;
        }

        var agent = agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
        return await ContextProviderPipeline
            .InvokeAsync(input.AgentName, agent, providers, toolRegistry, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task NotifyProvidersInvokedAsync(
        ContextProviderPipeline.Contribution contribution,
        List<ChatMessage> requestMessages,
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        foreach (var provider in contribution.Providers)
        {
            try
            {
#pragma warning disable MAAI001 // AIContextProvider context types are experimental (evaluation only).
                var invokedContext = new AIContextProvider.InvokedContext(
                    contribution.Agent, contribution.Session, requestMessages, response.Messages);
#pragma warning restore MAAI001
                await provider.InvokedAsync(invokedContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogContextProviderInvokedError(ex.Message);
            }
        }
    }

    private static List<ChatMessage> BuildChatMessages(
        string? instructions,
        ContextProviderPipeline.Contribution? contribution,
        List<WorkflowChatMessage> messages)
    {
        var chatMessages = new List<ChatMessage>();

        var systemText = instructions;
        if (contribution is { Instructions.Count: > 0 })
        {
            var providerInstructions = string.Join("\n\n", contribution.Instructions);
            systemText = string.IsNullOrWhiteSpace(systemText)
                ? providerInstructions
                : $"{systemText}\n\n{providerInstructions}";
        }

        if (!string.IsNullOrWhiteSpace(systemText))
        {
            chatMessages.Add(new ChatMessage(ChatRole.System, systemText));
        }

        if (contribution is { Messages.Count: > 0 })
        {
            chatMessages.AddRange(contribution.Messages);
        }

        foreach (var msg in messages)
        {
            chatMessages.Add(ConvertToChatMessage(msg));
        }

        return chatMessages;
    }

    private static ChatOptions? BuildChatOptions(
        IList<AITool>? tools,
        ContextProviderPipeline.Contribution? contribution,
        AgentRunOptions? agentRunOptions)
    {
        var mergedTools = new List<AITool>();
        if (tools is { Count: > 0 })
        {
            mergedTools.AddRange(tools);
        }

        if (contribution is { Tools.Count: > 0 })
        {
            mergedTools.AddRange(contribution.Tools);
        }

        if (mergedTools.Count == 0 && agentRunOptions is null)
        {
            return null;
        }

#pragma warning disable MEAI001
        return new ChatOptions
        {
            AllowBackgroundResponses = agentRunOptions?.AllowBackgroundResponses,
            AdditionalProperties = agentRunOptions?.AdditionalProperties,
            ContinuationToken = agentRunOptions?.ContinuationToken,
            ResponseFormat = agentRunOptions?.ResponseFormat,
            Tools = mergedTools.Count > 0 ? mergedTools : null
        };
#pragma warning restore MEAI001
    }

    private static CallLlmOutput ParseResponse(ChatResponse response)
    {
        var responseMessage = response.Messages[^1];
        var functionCalls = responseMessage.Contents.OfType<FunctionCallContent>().ToList();
        var text = responseMessage.Text;

        var isFinal = functionCalls.Count == 0;

        return new CallLlmOutput
        {
            IsFinal = isFinal,
            Text = text,
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

    private static ChatMessage ConvertToChatMessage(WorkflowChatMessage msg)
    {
        var role = msg.Role switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        var contents = new List<AIContent>();

        if (msg.Content is not null)
        {
            contents.Add(new TextContent(msg.Content));
        }

        if (msg.FunctionCalls is { Count: > 0 })
        {
            foreach (var fc in msg.FunctionCalls)
            {
                var args = string.IsNullOrEmpty(fc.ArgumentsJson) || fc.ArgumentsJson == "{}"
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.ArgumentsJson);
                contents.Add(new FunctionCallContent(fc.CallId, fc.Name, args));
            }
        }

        if (msg.FunctionResults is { Count: > 0 })
        {
            foreach (var fr in msg.FunctionResults)
            {
                object? result = fr.ResultJson is not null
                    ? JsonSerializer.Deserialize<JsonElement>(fr.ResultJson)
                    : null;
                contents.Add(new FunctionResultContent(fr.CallId, result));
            }
        }

        return new ChatMessage(role, contents);
    }

    [LoggerMessage(LogLevel.Information, "Calling LLM for agent '{AgentName}' with {MessageCount} messages")]
    private partial void LogLlmCallInfo(string agentName, int messageCount);

    [LoggerMessage(LogLevel.Error, "LLM call failed for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogLlmCallError(string agentName, string errorMessage);

    [LoggerMessage(LogLevel.Warning, "Context provider InvokedAsync callback failed: {ErrorMessage}")]
    private partial void LogContextProviderInvokedError(string errorMessage);
}
