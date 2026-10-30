// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// Custom workflow that invokes EchoAgent via <see cref="WorkflowContextExtensions.GetAgent"/>
/// and <see cref="WorkflowContextExtensions.RunAgentAsync"/>, exercising the workflow-context path.
/// </summary>
internal sealed class EchoOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var agent    = context.GetAgent("EchoAgent");
        var response = await context.RunAgentAsync(agent, message: input);
        return response.Text ?? string.Empty;
    }
}

/// <summary>
/// Custom workflow that starts EchoAgent as a detached run via
/// <see cref="WorkflowContextExtensions.StartAgentRunDetachedAsync"/> and completes immediately with the
/// run's instance ID, without waiting for the run.
/// </summary>
internal sealed class DetachedEchoOrchestrationWorkflow : Workflow<string, string>
{
    public override Task<string> RunAsync(WorkflowContext context, string input) =>
        context.StartAgentRunDetachedAsync(context.GetAgent("EchoAgent"), message: input);
}

/// <summary>
/// Custom workflow that invokes CapitalAgent and deserializes its JSON response via
/// <see cref="WorkflowContextExtensions.RunAgentAndDeserializeAsync{T}"/>.
/// </summary>
internal sealed class CapitalOrchestrationWorkflow : Workflow<string, CapitalAnswer?>
{
    public override async Task<CapitalAnswer?> RunAsync(WorkflowContext context, string input)
    {
        var agent = context.GetAgent("CapitalAgent");
        return await context.RunAgentAndDeserializeAsync<CapitalAnswer>(agent, message: input);
    }
}

/// <summary>
/// Custom workflow that resolves a keyed agent (AlphaAgent / "chat-key-alpha") via
/// <see cref="WorkflowContextExtensions.GetAgent(WorkflowContext, string, string)"/>.
/// </summary>
internal sealed class KeyedOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var agent    = context.GetAgent("AlphaAgent", "chat-key-alpha");
        var response = await context.RunAgentAsync(agent, message: input);
        return response.Text ?? string.Empty;
    }
}

/// <summary>
/// Custom workflow that runs <see cref="ReasoningMockChatClient.AgentName"/> for two turns with
/// <see cref="WorkflowContextExtensions.RunAgentWithHistoryAsync"/>, passing the first turn's
/// messages as the second turn's history. Returns
/// <c>{turn 1 text}|{turn 2 text}|{reasoning on the turn 2 response}</c>.
/// </summary>
internal sealed class ReasoningHistoryOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var agent = context.GetAgent(ReasoningMockChatClient.AgentName);

        var first = await context.RunAgentWithHistoryAsync(agent, $"{input} (turn 1)");
        var second = await context.RunAgentWithHistoryAsync(agent, $"{input} (turn 2)", first.TurnMessages);

        var finalReasoning = second.Response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Select(r => $"{r.Text}/{r.ProtectedData}");

        return $"{first.Response.Text}|{second.Response.Text}|{string.Join(",", finalReasoning)}";
    }
}

/// <summary>
/// Runs <see cref="ReasoningMockChatClient"/> twice against the stored conversation named by the input, with
/// <see cref="WorkflowContextExtensions.RunAgentInConversationAsync"/>, so turn 2 gets turn 1 from the store.
/// </summary>
internal sealed class ConversationReasoningOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string conversationId)
    {
        var agent = context.GetAgent(ReasoningMockChatClient.AgentName);

        var first = await context.RunAgentInConversationAsync(agent, conversationId, "turn 1");
        var second = await context.RunAgentInConversationAsync(agent, conversationId, "turn 2");

        var finalReasoning = second.Response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Select(r => $"{r.Text}/{r.ProtectedData}");

        return $"{first.Response.Text}|{second.Response.Text}|{string.Join(",", finalReasoning)}|{first.ConversationSequence},{second.ConversationSequence}";
    }
}

/// <summary>
/// Runs <see cref="FailOnceChatClient"/> against a stored conversation: the first attempt fails after its tool
/// call, and the retry with the same conversation id succeeds.
/// </summary>
internal sealed class ConversationRetryOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string conversationId)
    {
        var agent = context.GetAgent(FailOnceChatClient.AgentName);
        var message = $"{FailOnceChatClient.Marker} {conversationId}";

        var failed = false;
        try
        {
            await context.RunAgentInConversationAsync(agent, conversationId, message);
        }
        catch (Exception)
        {
            failed = true;
        }

        var retry = await context.RunAgentInConversationAsync(agent, conversationId, message);
        return $"{failed}|{retry.ConversationSequence}|{retry.Response.Text}";
    }
}

/// <summary>
/// Runs EchoAgent against a stored conversation, then deletes it twice with
/// <see cref="WorkflowContextExtensions.DeleteConversationAsync"/>.
/// </summary>
internal sealed class DeleteConversationOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string conversationId)
    {
        await context.RunAgentInConversationAsync(context.GetAgent("EchoAgent"), conversationId, "hello");
        var deleted = await context.DeleteConversationAsync(conversationId);
        var deletedAgain = await context.DeleteConversationAsync(conversationId);
        return $"{deleted}|{deletedAgain}";
    }
}

/// <summary>
/// Runs EchoAgent twice at once against the stored conversation named by the input.
/// </summary>
internal sealed class ConcurrentConversationOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string conversationId)
    {
        var agent = context.GetAgent("EchoAgent");
        var results = await Task.WhenAll(
            context.RunAgentInConversationAsync(agent, conversationId, "first"),
            context.RunAgentInConversationAsync(agent, conversationId, "second"));

        return string.Join(";", results.Select(r => $"{r.ConversationSequence},{r.ConversationBaseMoved}"));
    }
}

/// <summary>
/// Runs <see cref="CompactionMockChatClient"/> for three turns against the stored conversation named by the input.
/// The agent compacts after a run whose loaded history is past its small trigger.
/// </summary>
internal sealed class CompactingConversationOrchestrationWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string conversationId)
    {
        var agent = context.GetAgent(CompactionMockChatClient.AgentName);
        var padding = new string('.', 200);

        for (var i = 1; i <= 3; i++)
        {
            await context.RunAgentInConversationAsync(agent, conversationId, $"{conversationId} turn {i} {padding}");
        }

        return "done";
    }
}
