// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates the session implementation: <see cref="DaprSessionExtensions.CreateSessionAsync"/>,
/// <see cref="DaprSessionExtensions.AttachSession"/>, <see cref="DaprSessionExtensions.GetSessionInstanceId"/>,
/// and multi-turn conversation continuity via <see cref="Diagrid.AI.Microsoft.AgentFramework.Runtime.SessionWorkflow"/>.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class SessionTests(DaprFixture fixture)
{
    // ── Session creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_Returns_SessionWithNonEmptyInstanceId()
    {
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);

        var instanceId = session.GetSessionInstanceId();

        Assert.NotNull(instanceId);
        Assert.NotEmpty(instanceId);
    }

    [Fact]
    public async Task CreateSessionAsync_EachCall_Returns_UniqueInstanceId()
    {
        var session1 = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var session2 = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);

        Assert.NotEqual(session1.GetSessionInstanceId(), session2.GetSessionInstanceId());
    }

    // ── Session attachment and ID round-trip ──────────────────────────────────

    [Fact]
    public async Task AttachSession_WithExistingId_ReturnsSessionWithSameId()
    {
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var originalId = session.GetSessionInstanceId()!;

        var reattached = fixture.Invoker.AttachSession(originalId);

        Assert.Equal(originalId, reattached.GetSessionInstanceId());
    }

    [Fact]
    public void GetSessionInstanceId_OnAttachedSession_ReturnsExpectedId()
    {
        // Validates that a known ID survives the StateBag round-trip without modification.
        const string knownId = "test-session-id-abc123";
        var session = fixture.Invoker.AttachSession(knownId);

        Assert.Equal(knownId, session.GetSessionInstanceId());
    }

    // ── Single-turn session invocation ────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithSession_SingleTurn_ReturnsExpectedResponse()
    {
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        var response = await fixture.Invoker.RunAgentAsync(agent, "hello", session);

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    // ── Multi-turn session invocation ─────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithSession_SequentialTurns_AllReturnExpectedResponse()
    {
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        for (var i = 0; i < 3; i++)
        {
            var response = await fixture.Invoker.RunAgentAsync(agent, $"turn {i}", session);
            Assert.NotNull(response);
            Assert.Equal("Hello from EchoAgent!", response.Text);
        }
    }

    [Fact]
    public async Task RunAgentAsync_WithSession_SecondTurn_ReceivesConversationHistory()
    {
        // The CallLlmActivity builds: [system, ...priorMessages, currentUserMessage].
        // Turn 1 (no prior history): [system, user] = 2 messages.
        // Turn 2 (with turn-1 history): [system, user1, assistant1, user2] = 4 messages.
        fixture.HistoryRecorder.Reset();

        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var agent = fixture.Invoker.GetAgent("HistoryAgent");

        await fixture.Invoker.RunAgentAsync(agent, "first message", session);
        await fixture.Invoker.RunAgentAsync(agent, "second message", session);

        var counts = fixture.HistoryRecorder.Counts;
        Assert.Equal(2, counts.Count);
        Assert.True(counts[1] > counts[0],
            $"Second turn should receive more messages (got {counts[1]}) than first turn (got {counts[0]}).");
    }

    // ── Session re-attachment ─────────────────────────────────────────────────

    [Fact]
    public async Task AttachSession_AfterFirstTurn_ContinuesConversationHistory()
    {
        // Verifies that AttachSession(id) re-connects to the running SessionWorkflow,
        // which still holds the accumulated conversation log from the first turn.
        fixture.HistoryRecorder.Reset();

        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var sessionId = session.GetSessionInstanceId()!;
        var agent = fixture.Invoker.GetAgent("HistoryAgent");

        // Turn 1 using the original session reference.
        await fixture.Invoker.RunAgentAsync(agent, "first message", session);

        // Re-attach by ID and issue a second turn from the new reference.
        var reattached = fixture.Invoker.AttachSession(sessionId);
        await fixture.Invoker.RunAgentAsync(agent, "second message", reattached);

        var counts = fixture.HistoryRecorder.Counts;
        Assert.Equal(2, counts.Count);
        Assert.True(counts[1] > counts[0],
            $"Re-attached session should carry prior history into turn 2 (turn 2 got {counts[1]}, turn 1 got {counts[0]}).");
    }

    // ── Keyed agent via session ───────────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithSession_KeyedAgent_ReturnsExpectedResponse()
    {
        // AlphaAgent is registered under chat-client key "chat-key-alpha".
        // The key is forwarded through SessionTurnRequest → DaprAgentInvocation → CallLlmActivity.
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient);
        var agent = fixture.Invoker.GetAgent("AlphaAgent");

        var response = await fixture.Invoker.RunAgentAsync(agent, "hello from session", session);

        Assert.NotNull(response);
        Assert.Equal("Alpha response", response.Text);
    }

    // ── MaxTurns session lifecycle ────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_WithMaxTurns_SessionWorkflowCompletesAfterLimit()
    {
        // SessionWorkflow exits the loop once turnCount reaches maxTurns, completing the workflow.
        const uint maxTurns = 2;
        var session = await fixture.Invoker.CreateSessionAsync(fixture.WorkflowClient, maxTurns: maxTurns);
        var sessionId = session.GetSessionInstanceId()!;
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        // Exhaust the turn limit — all turns should succeed normally.
        for (var i = 0; i < maxTurns; i++)
        {
            var response = await fixture.Invoker.RunAgentAsync(agent, $"turn {i}", session);
            Assert.Equal("Hello from EchoAgent!", response.Text);
        }

        // Poll until the session workflow transitions to Completed (it exits the loop after
        // the last turn's custom status is set).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        WorkflowRuntimeStatus? finalStatus = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await fixture.WorkflowClient.GetWorkflowStateAsync(sessionId);
            finalStatus = state?.RuntimeStatus;
            if (finalStatus == WorkflowRuntimeStatus.Completed)
                break;
            await Task.Delay(500);
        }

        Assert.Equal(WorkflowRuntimeStatus.Completed, finalStatus);
    }
}
