// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates agent runs against a stored conversation: <c>RunAgentInConversationAsync</c> and
/// <c>DeleteConversationAsync</c> inside workflows, and <c>AttachConversation</c> through the invoker.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class ConversationRunTests(DaprFixture fixture)
{
    private static string NewId(string name) => $"{name}-{Guid.NewGuid():N}";

    [Fact]
    public async Task RunAgentInConversationAsync_TheSecondRunSeesTheFirstFromTheStore()
    {
        var conversationId = NewId("conversation-reasoning");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(ConversationReasoningOrchestrationWorkflow),
            input: conversationId);
        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        // The same reasoning flow as RunAgentWithHistoryAsync, with turn 1 loaded from the store instead of
        // carried in TurnMessages: turn 2's calls see turn 1's two blocks, then their own.
        Assert.Equal(
            "seen:thought-1|seen:thought-1,thought-2,thought-3|thought-4/sig-4|4,8",
            state.ReadOutputAs<string>());

        var stored = await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(8, stored.NextSequence);
        Assert.Equal(
            ["user", "assistant", "tool", "assistant", "user", "assistant", "tool", "assistant"],
            stored.Entries.Select(e => e.Message.Role));
        Assert.Equal("turn 1", stored.Entries[0].Message.Content);
        Assert.Equal("thought-1", Assert.Single(stored.Entries[1].Message.Reasoning!).Text);
        Assert.Single(stored.Entries[1].Message.FunctionCalls!);
        Assert.Single(stored.Entries[2].Message.FunctionResults!);
        Assert.Equal(2, stored.Entries.Select(e => e.TurnId).Distinct().Count());
    }

    [Fact]
    public async Task RunAgentInConversationAsync_AFailedRunStoresNothing_AndItsRetryStoresOneTurn()
    {
        var conversationId = NewId("conversation-retry");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(ConversationRetryOrchestrationWorkflow),
            input: conversationId);
        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        // The retry saw no stored history, so it reasoned from scratch, and only its own 4 messages are stored.
        Assert.Equal("True|4|seen:thought-1", state.ReadOutputAs<string>());

        var stored = await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, stored.NextSequence);
        Assert.Single(stored.Entries.Select(e => e.TurnId).Distinct());
    }

    [Fact]
    public async Task DeleteConversationAsync_DeletesFromAWorkflow()
    {
        var conversationId = NewId("conversation-delete");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(DeleteConversationOrchestrationWorkflow),
            input: conversationId);
        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        Assert.Equal("True|False", state.ReadOutputAs<string>());
        Assert.Empty((await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries);
    }

    [Fact]
    public async Task AttachConversation_RunsThroughTheInvokerContinueTheConversation()
    {
        var conversationId = NewId("conversation-invoker");
        var session = fixture.Invoker.AttachConversation(conversationId);
        var agent = fixture.Invoker.GetAgent(ReasoningMockChatClient.AgentName);

        var first = await fixture.Invoker.RunAgentAsync(agent, "turn 1", session);
        var second = await fixture.Invoker.RunAgentAsync(agent, "turn 2", session);

        Assert.Equal("seen:thought-1", first.Text);
        Assert.Equal("seen:thought-1,thought-2,thought-3", second.Text);
        var stored = await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(8, stored.NextSequence);
    }

    [Fact]
    public async Task RunAgentInConversationAsync_ConcurrentRuns_BothTurnsAreStoredWhole()
    {
        var conversationId = NewId("conversation-concurrent");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(ConcurrentConversationOrchestrationWorkflow),
            input: conversationId);
        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        var runs = state.ReadOutputAs<string>()!.Split(';').Select(r => r.Split(',')).ToList();
        Assert.Equal(["2", "4"], runs.Select(r => r[0]).Order());
        // The run stored first saw everything before it; the other one did only if it started after that commit.
        Assert.Equal("False", runs.Single(r => r[0] == "2")[1]);

        var stored = await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, stored.NextSequence);
        Assert.Equal(stored.Entries[0].TurnId, stored.Entries[1].TurnId);
        Assert.Equal(stored.Entries[2].TurnId, stored.Entries[3].TurnId);
        Assert.NotEqual(stored.Entries[0].TurnId, stored.Entries[2].TurnId);
    }

    [Fact]
    public async Task Compaction_SummarizesOlderTurns_AndTheNextRunGetsTheSummary()
    {
        var conversationId = NewId("conversation-compaction");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(CompactingConversationOrchestrationWorkflow),
            input: conversationId);
        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);
        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);

        // Turn 2 loaded turn 1 (past the trigger), so turn 1 was summarized after it; turn 3 loaded the summary
        // and turn 2, and then turn 2 was summarized too.
        var stored = await fixture.ConversationStore.LoadAsync(conversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stored.Entries[0].SummaryOf);
        Assert.EndsWith(CompactionMockChatClient.Summary, stored.Entries[0].Message.Content);
        Assert.Equal(["system", "user", "assistant"], stored.Entries.Select(e => e.Message.Role));
        Assert.Contains("turn 3", stored.Entries[1].Message.Content);

        var turn3Call = CompactionMockChatClient.Requests.Single(r => r[^1].Contains($"{conversationId} turn 3", StringComparison.Ordinal));
        Assert.Contains(turn3Call, m => m.StartsWith("system: ", StringComparison.Ordinal) && m.EndsWith(CompactionMockChatClient.Summary, StringComparison.Ordinal));
        Assert.DoesNotContain(turn3Call, m => m.Contains($"{conversationId} turn 1", StringComparison.Ordinal));
        Assert.Contains(turn3Call, m => m.Contains($"{conversationId} turn 2", StringComparison.Ordinal));
    }
}
