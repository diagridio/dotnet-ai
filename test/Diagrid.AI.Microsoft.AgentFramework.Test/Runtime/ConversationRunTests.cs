// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies agent runs against a stored conversation: the activity sequence of <see cref="AgentRunWorkflow"/>
/// with and without one, the history <see cref="CallLlmActivity"/> loads, and the commit activities.
/// </summary>
public sealed class ConversationRunTests
{
    private const string AgentName = "conversation-agent";
    private const string ConversationId = "conv-1";

    private static readonly string Resolve = nameof(ResolveAgentContextActivity);
    private static readonly string CallLlm = nameof(CallLlmActivity);
    private static readonly string ExecuteTool = nameof(ExecuteToolActivity);
    private static readonly string Append = nameof(AppendConversationTurnActivity);
    private static readonly string Complete = nameof(CompleteAgentContextActivity);

    private static CallLlmOutput Final(string text = "done", long? historySequence = null, string? incarnation = null) =>
        new() { IsFinal = true, Text = text, HistorySequence = historySequence, HistoryIncarnation = incarnation };

    private static CallLlmOutput ToolCall(long? historySequence = null, string? incarnation = null) => new()
    {
        IsFinal = false,
        FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "tool" }],
        HistorySequence = historySequence,
        HistoryIncarnation = incarnation,
    };

    /// <summary>
    /// A workflow context whose LLM calls return <paramref name="llmOutputs"/> in order (the last one repeats).
    /// </summary>
    private static TestWorkflowContext MakeContext(params CallLlmOutput[] llmOutputs)
    {
        var call = 0;
        return new TestWorkflowContext("wf-1", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
            nameof(ExecuteToolActivity) => Task.FromResult<object?>(new ExecuteToolOutput { CallId = "c1", FunctionName = "tool", ResultJson = "1" }),
            nameof(AppendConversationTurnActivity) => Task.FromResult<object?>(
                new ConversationAppendResult(ConversationAppendStatus.Appended, 5, 5 + ((AppendConversationTurnInput)input!).Messages.Count, false)),
            _ => Task.FromResult<object?>(llmOutputs[Math.Min(call++, llmOutputs.Length - 1)]),
        });
    }

    private static List<string> Names(TestWorkflowContext context) => context.ActivityCalls.Select(c => c.Name).ToList();

    // ── Without a conversation: the activity sequence is what it always was ──

    [Fact]
    public async Task Golden_WithoutAConversation_FinalPath()
    {
        var context = MakeContext(Final());

        await new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "hi", null, null));

        Assert.Equal([Resolve, CallLlm, Complete], Names(context));
        Assert.Equal(0, context.NewGuidCalls);
        Assert.All(context.ActivityCalls, c => Assert.Null(c.Options));
    }

    [Fact]
    public async Task Golden_WithoutAConversation_ToolLoopPath()
    {
        var context = MakeContext(ToolCall(), Final());

        var result = await new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "hi", null, null));

        Assert.Equal([Resolve, CallLlm, ExecuteTool, CallLlm, Complete], Names(context));
        Assert.Equal(0, context.NewGuidCalls);
        Assert.Null(result.ConversationId);
        Assert.Null(result.ConversationSequence);
        Assert.All(context.ActivityCalls.Where(c => c.Name == CallLlm), c =>
        {
            var input = (CallLlmInput)c.Input!;
            Assert.Null(input.ConversationId);
            Assert.Null(input.HistoryUpToSequence);
            Assert.Null(input.HistoryIncarnation);
        });
    }

    [Fact]
    public async Task Golden_WithoutAConversation_MaxIterationsPath()
    {
        var context = MakeContext(ToolCall());

        await new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "hi", null, null));

        var expected = new List<string> { Resolve };
        for (var i = 0; i < 20; i++)
        {
            expected.Add(CallLlm);
            expected.Add(ExecuteTool);
        }

        expected.Add(Complete);
        Assert.Equal(expected, Names(context));
        Assert.Equal(0, context.NewGuidCalls);
    }

    [Fact]
    public async Task Golden_WithoutAConversation_FailurePath()
    {
        var context = MakeContext(new CallLlmOutput { Error = "boom" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "hi", null, null)));

        Assert.Equal([Resolve, CallLlm, Complete], Names(context));
        Assert.Equal(0, context.NewGuidCalls);
    }

    // ── With a conversation ────────────────────────────────────────────────

    [Fact]
    public async Task Conversation_PinsTheFirstCallsHistory_AndCommitsTheTurnBeforeNotifyingProviders()
    {
        // The second output reports a longer history: only the first call's counts.
        var context = MakeContext(ToolCall(historySequence: 5, incarnation: "inc-a"), Final(historySequence: 9, incarnation: "inc-b"));
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null) { ChatClientKey = "key", ConversationId = ConversationId };

        var result = await new AgentRunWorkflow().RunAsync(context, invocation);

        Assert.Equal([Resolve, CallLlm, ExecuteTool, CallLlm, Append, Complete], Names(context));
        var llmInputs = context.ActivityCalls.Where(c => c.Name == CallLlm).Select(c => (CallLlmInput)c.Input!).ToList();
        Assert.All(llmInputs, i => Assert.Equal(ConversationId, i.ConversationId));
        Assert.Equal((null, null), (llmInputs[0].HistoryUpToSequence, llmInputs[0].HistoryIncarnation));
        Assert.Equal((5L, "inc-a"), (llmInputs[1].HistoryUpToSequence, llmInputs[1].HistoryIncarnation));
        // The run's own messages only, starting with its user message: the history is loaded by the
        // activity, not carried. (The list is shared by every call of the run, so it holds the whole run now.)
        Assert.Equal("hi", llmInputs[0].Messages[0].Content);
        Assert.Equal(4, llmInputs[0].Messages.Count);
        Assert.Equal(0, llmInputs[0].RunMessagesStart);

        var append = context.ActivityCalls.Single(c => c.Name == Append);
        var appendInput = (AppendConversationTurnInput)append.Input!;
        Assert.Equal(ConversationId, appendInput.ConversationId);
        Assert.Matches("^wf-1:[0-9a-f]{32}$", appendInput.TurnId);
        Assert.Equal((5L, "inc-a"), (appendInput.BaseSequence, appendInput.BaseIncarnation));
        Assert.Equal((AgentName, "key"), (appendInput.AgentName, appendInput.ChatClientKey));
        Assert.Same(result.TurnMessages, appendInput.Messages);
        Assert.Equal(["user", "assistant", "tool", "assistant"], appendInput.Messages.Select(m => m.Role));

        var retry = append.Options!.RetryPolicy!;
        Assert.Equal(5, retry.MaxNumberOfAttempts);

        Assert.Equal(1, context.NewGuidCalls);
        Assert.Equal(ConversationId, result.ConversationId);
        Assert.Equal(9, result.ConversationSequence);
    }

    [Fact]
    public async Task Conversation_MaxIterations_CommitsTheTurnMessagesEndingWithToolResults()
    {
        var context = MakeContext(ToolCall(historySequence: 2));
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId };

        var result = await new AgentRunWorkflow().RunAsync(context, invocation);

        var names = Names(context);
        Assert.Equal([Append, Complete], names.TakeLast(2));
        var appendInput = (AppendConversationTurnInput)context.ActivityCalls.Single(c => c.Name == Append).Input!;
        Assert.Same(result.TurnMessages, appendInput.Messages);
        Assert.Equal("tool", appendInput.Messages[^1].Role);
        Assert.Equal(41, appendInput.Messages.Count);
    }

    [Fact]
    public async Task Conversation_AFailedRunCommitsNothing()
    {
        var context = MakeContext(ToolCall(historySequence: 2), new CallLlmOutput { Error = "boom" });
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentRunWorkflow().RunAsync(context, invocation));

        Assert.DoesNotContain(Append, Names(context));
        Assert.Equal(Complete, Names(context)[^1]);
    }

    [Fact]
    public async Task Conversation_WithPriorMessages_IsRejectedBeforeAnyActivity()
    {
        var context = MakeContext(Final());
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null)
        {
            ConversationId = ConversationId,
            PriorMessages = [new WorkflowChatMessage { Role = "user", Content = "old" }],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => new AgentRunWorkflow().RunAsync(context, invocation));

        Assert.Empty(context.ActivityCalls);
    }

    [Fact]
    public async Task Conversation_FirstOutputWithoutAHistorySequence_CommitsAgainstZero()
    {
        var context = MakeContext(Final());
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId };

        await new AgentRunWorkflow().RunAsync(context, invocation);

        var appendInput = (AppendConversationTurnInput)context.ActivityCalls.Single(c => c.Name == Append).Input!;
        Assert.Equal(0, appendInput.BaseSequence);
        Assert.Null(appendInput.BaseIncarnation);
    }

    // ── CallLlmActivity loads the history ──────────────────────────────────

    [Fact]
    public async Task CallLlm_SendsSystemThenProviderMessagesThenHistoryThenTheRun()
    {
        var store = new RecordingStore(
            Entry(0, new WorkflowChatMessage { Role = "user", Content = "earlier question" }),
            Entry(1, new WorkflowChatMessage
            {
                Role = "assistant",
                Content = "earlier answer",
                Reasoning =
                [
                    new WorkflowReasoningContent { Text = "mine", Source = AgentName },
                    new WorkflowReasoningContent { Text = "another agent's", Source = "other-agent" },
                ],
            }));
        var (activity, client) = BuildActivity(store, instructions: "be brief");
        client.SetNextResponse(finalText: "ok");

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null,
            [new WorkflowChatMessage { Role = "user", Content = "now" }])
        {
            ConversationId = ConversationId,
            AdditionalMessages = [new WorkflowChatMessage { Role = "user", Content = "provider context" }],
        });

        var sent = client.LastMessages!;
        Assert.Equal(["be brief", "provider context", "earlier question", "earlier answer", "now"], sent.Select(m => m.Text));
        var reasoning = Assert.Single(sent[3].Contents.OfType<TextReasoningContent>());
        Assert.Equal("mine", reasoning.Text);
        Assert.Equal((2L, "inc-1"), (output.HistorySequence, output.HistoryIncarnation));
        Assert.Equal((ConversationId, (long?)null, (string?)null), store.LastLoad);
    }

    [Fact]
    public async Task CallLlm_LoadsThePinnedPrefix()
    {
        var store = new RecordingStore();
        var (activity, client) = BuildActivity(store);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null,
            [new WorkflowChatMessage { Role = "user", Content = "now" }])
        {
            ConversationId = ConversationId,
            HistoryUpToSequence = 4,
            HistoryIncarnation = "inc-1",
        });

        Assert.Equal((ConversationId, (long?)4, "inc-1"), store.LastLoad);
    }

    [Fact]
    public async Task CallLlm_WithinRun_DoesNotSendTheHistorysReasoning()
    {
        var store = new RecordingStore(Entry(0, new WorkflowChatMessage
        {
            Role = "assistant",
            Content = "earlier",
            Reasoning = [new WorkflowReasoningContent { Text = "old", Source = AgentName }],
        }));
        var (activity, client) = BuildActivity(store, replay: ReasoningReplay.WithinRun);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "now" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = "c", Name = "t" }],
                Reasoning = [new WorkflowReasoningContent { Text = "this run", Source = AgentName }],
            },
            new WorkflowChatMessage { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = "c", Name = "t" }] },
        ])
        {
            ConversationId = ConversationId,
            RunMessagesStart = 0,
        });

        Assert.Equal(["this run"], client.LastMessages!.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(r => r.Text));
    }

    [Fact]
    public async Task CallLlm_WithoutAStore_ExplainsHowToRegisterOne()
    {
        var (activity, _) = BuildActivity(store: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(
            new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "now" }]) { ConversationId = ConversationId }));

        Assert.Contains("WithConversationStore", ex.Message, StringComparison.Ordinal);
    }

    // ── The commit activities ──────────────────────────────────────────────

    [Fact]
    public async Task AppendActivity_PassesTheTurnToTheStore()
    {
        var store = new RecordingStore();
        var activity = new AppendConversationTurnActivity(NullLogger<AppendConversationTurnActivity>.Instance, store);
        var messages = new List<WorkflowChatMessage> { new() { Role = "user", Content = "hi" } };

        var result = await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new AppendConversationTurnInput(ConversationId, "wf:1", 3, AgentName, "key", messages) { BaseIncarnation = "inc-1" });

        Assert.Equal(new ConversationTurn(ConversationId, "wf:1", 3, AgentName, "key", messages, "inc-1"), store.LastAppend);
        Assert.Equal(ConversationAppendStatus.Appended, result.Status);
    }

    [Fact]
    public async Task CommitActivities_WithoutAStore_ExplainHowToRegisterOne()
    {
        var append = new AppendConversationTurnActivity(NullLogger<AppendConversationTurnActivity>.Instance);
        var delete = new DeleteConversationActivity();

        var appendError = await Assert.ThrowsAsync<InvalidOperationException>(() => append.RunAsync(
            new TestWorkflowActivityContext("i"), new AppendConversationTurnInput(ConversationId, "t", 0, AgentName, null, [])));
        var deleteError = await Assert.ThrowsAsync<InvalidOperationException>(() => delete.RunAsync(
            new TestWorkflowActivityContext("i"), ConversationId));

        Assert.Contains("WithConversationStore", appendError.Message, StringComparison.Ordinal);
        Assert.Contains("WithConversationStore", deleteError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteConversationAsync_SchedulesTheDeleteActivity()
    {
        var context = new TestWorkflowContext("wf-1", (_, _) => Task.FromResult<object?>(true));

        Assert.True(await context.DeleteConversationAsync(ConversationId));

        Assert.Equal((nameof(DeleteConversationActivity), (object?)ConversationId), (context.ActivityCalls[0].Name, context.ActivityCalls[0].Input));
    }

    [Fact]
    public async Task RunAgentInConversationAsync_SchedulesTheRunWithTheConversation()
    {
        DaprAgentInvocation? scheduled = null;
        var context = new TestWorkflowContext("wf-1", (name, input) =>
        {
            scheduled = (DaprAgentInvocation)input!;
            return Task.FromResult<object?>(new AgentRunResult());
        });

        await context.RunAgentInConversationAsync(context.GetAgent(AgentName, "key"), ConversationId, "hi");

        Assert.Equal((AgentName, "key", ConversationId, "hi"), (scheduled!.AgentName, scheduled.ChatClientKey, scheduled.ConversationId, scheduled.Message));
        Assert.Null(scheduled.PriorMessages);
        await Assert.ThrowsAsync<ArgumentException>(() => context.RunAgentInConversationAsync(context.GetAgent(AgentName), " "));
    }

    [Fact]
    public void AttachConversation_PutsOnlyTheIdInTheSession()
    {
        var session = new Moq.Mock<IDaprAgentInvoker>().Object.AttachConversation(ConversationId);

        Assert.Equal(ConversationId, session.GetConversationId());
        Assert.Null(session.GetSessionInstanceId());
        Assert.Null(((AgentSession?)null).GetConversationId());
        Assert.Throws<ArgumentException>(() => new Moq.Mock<IDaprAgentInvoker>().Object.AttachConversation(""));
    }

    // ── Review regressions ─────────────────────────────────────────────────

    [Fact]
    public async Task Conversation_AStoredTurn_IsNotFailedByTheProvidersNotification()
    {
        // A failed run is retried as a new run with a new turn id, so failing here would store the turn twice.
        var context = new TestWorkflowContext("wf-1", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => throw new WorkflowTaskFailedException("failed",
                new WorkflowTaskFailureDetails(nameof(InvalidOperationException), "session lost", null)),
            nameof(AppendConversationTurnActivity) => Task.FromResult<object?>(
                new ConversationAppendResult(ConversationAppendStatus.Appended, 5, 7, false)),
            _ => Task.FromResult<object?>(Final(historySequence: 5, incarnation: "inc")),
        });

        var result = await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId });

        Assert.Equal("done", result.Response.Text);
        Assert.Equal([Resolve, CallLlm, Append, Complete], Names(context));
        Assert.Equal(7, result.ConversationSequence);
    }

    [Fact]
    public async Task Golden_WithoutAConversation_AFailedNotificationStillFailsTheRun()
    {
        var context = new TestWorkflowContext("wf-1", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => throw new WorkflowTaskFailedException("failed",
                new WorkflowTaskFailureDetails(nameof(InvalidOperationException), "session lost", null)),
            _ => Task.FromResult<object?>(Final()),
        });

        await Assert.ThrowsAsync<WorkflowTaskFailedException>(() =>
            new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "hi", null, null)));
    }

    [Fact]
    public async Task Conversation_ARedeliveredCommit_ReportsWhereThisRunsTurnEnds()
    {
        // The commit landed, its reply was lost, another run appended, and the redelivery found the turn.
        var context = new TestWorkflowContext("wf-1", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
            nameof(AppendConversationTurnActivity) => Task.FromResult<object?>(
                new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 5, 20, true)),
            _ => Task.FromResult<object?>(Final(historySequence: 3, incarnation: "inc")),
        });

        var result = await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId });

        Assert.Equal(5 + result.TurnMessages.Count, result.ConversationSequence);
        Assert.True(result.ConversationBaseMoved);
    }

    [Fact]
    public async Task Golden_WithPriorMessages_IsUnchanged()
    {
        var context = MakeContext(Final());
        var invocation = new DaprAgentInvocation(AgentName, "hi", null, null)
        {
            PriorMessages = [new WorkflowChatMessage { Role = "user", Content = "a" }, new WorkflowChatMessage { Role = "assistant", Content = "b" }],
        };

        var result = await new AgentRunWorkflow().RunAsync(context, invocation);

        Assert.Equal([Resolve, CallLlm, Complete], Names(context));
        Assert.Equal(0, context.NewGuidCalls);
        var llmInput = (CallLlmInput)context.ActivityCalls[1].Input!;
        Assert.Equal(2, llmInput.RunMessagesStart);
        Assert.Null(llmInput.ConversationId);
        Assert.Null(result.ConversationBaseMoved);
    }

    [Fact]
    public async Task AppendActivity_RunTwice_IsAppendedThenAlreadyAppended()
    {
        var store = new DaprConversationStore(
            new Conversations.InMemoryConversationStateBackend(),
            new DaprConversationStoreOptions { StateStoreName = "statestore" },
            TimeProvider.System) { Delay = (_, _) => Task.CompletedTask };
        var activity = new AppendConversationTurnActivity(NullLogger<AppendConversationTurnActivity>.Instance, store);
        var input = new AppendConversationTurnInput(ConversationId, "wf-1:abc", 0, AgentName, null,
            [new WorkflowChatMessage { Role = "user", Content = "hi" }, new WorkflowChatMessage { Role = "assistant", Content = "hello" }]);

        var first = await activity.RunAsync(new TestWorkflowActivityContext("i"), input);
        var second = await activity.RunAsync(new TestWorkflowActivityContext("i"), input);

        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.Appended, 0, 2, false), first);
        Assert.Equal(new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 0, 2, false), second);
    }

    [Fact]
    public async Task CallLlm_SendsBackStoredReasoningOnlyFromTheSameChatClientKey()
    {
        var store = new RecordingStore(Entry(0, new WorkflowChatMessage
        {
            Role = "assistant",
            Content = "earlier",
            Reasoning =
            [
                new WorkflowReasoningContent { Text = "same key", Source = $"{AgentName}/k1" },
                new WorkflowReasoningContent { Text = "other key", Source = $"{AgentName}/k2" },
                new WorkflowReasoningContent { Text = "no key", Source = AgentName },
            ],
        }));
        var (activity, client) = BuildActivity(store);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, "k1", [new WorkflowChatMessage { Role = "user", Content = "now" }]) { ConversationId = ConversationId });

        Assert.Equal(["same key"], client.LastMessages!.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(r => r.Text));
    }

    [Fact]
    public async Task RunAgentAsync_WithAnAttachedConversation_RunsAgainstIt()
    {
        DaprAgentInvocation? scheduled = null;
        var context = new TestWorkflowContext("wf-1", (_, input) =>
        {
            scheduled = (DaprAgentInvocation)input!;
            return Task.FromResult<object?>(new AgentRunResult());
        });
        var session = new Moq.Mock<IDaprAgentInvoker>().Object.AttachConversation(ConversationId);

        await context.RunAgentAsync(context.GetAgent(AgentName), "hi", session);

        Assert.Equal(ConversationId, scheduled!.ConversationId);
        Assert.Null(scheduled.Session);
    }

    [Fact]
    public void PayloadsFromBeforeConversations_DeserializeWithTheNewFieldsUnset()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        var invocation = System.Text.Json.JsonSerializer.Deserialize<DaprAgentInvocation>(
            """{"agentName":"a","message":"hi","session":null,"options":null}""", web)!;
        var llmInput = System.Text.Json.JsonSerializer.Deserialize<CallLlmInput>(
            """{"agentName":"a","chatClientKey":null,"messages":[]}""", web)!;
        var llmOutput = System.Text.Json.JsonSerializer.Deserialize<CallLlmOutput>("""{"isFinal":true,"text":"x"}""", web)!;
        var result = System.Text.Json.JsonSerializer.Deserialize<AgentRunResult>("""{"turnMessages":[]}""", web)!;

        Assert.Null(invocation.ConversationId);
        Assert.Equal(((string?)null, (long?)null, (string?)null), (llmInput.ConversationId, llmInput.HistoryUpToSequence, llmInput.HistoryIncarnation));
        Assert.Equal(((long?)null, (string?)null), (llmOutput.HistorySequence, llmOutput.HistoryIncarnation));
        Assert.Equal(((string?)null, (long?)null, (bool?)null), (result.ConversationId, result.ConversationSequence, result.ConversationBaseMoved));

        var append = new AppendConversationTurnInput("c", "t", 1, "a", null, [new WorkflowChatMessage { Role = "user", Content = "x" }]) { BaseIncarnation = "i" };
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<AppendConversationTurnInput>(System.Text.Json.JsonSerializer.Serialize(append, web), web)!;
        Assert.Equal(("c", "t", 1L, "i"), (roundTripped.ConversationId, roundTripped.TurnId, roundTripped.BaseSequence, roundTripped.BaseIncarnation));
        var appendResult = new ConversationAppendResult(ConversationAppendStatus.AlreadyAppended, 2, 4, true);
        Assert.Equal(appendResult, System.Text.Json.JsonSerializer.Deserialize<ConversationAppendResult>(System.Text.Json.JsonSerializer.Serialize(appendResult, web), web));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static ConversationEntry Entry(long sequence, WorkflowChatMessage message) =>
        new(sequence, "earlier-turn", AgentName, null, DateTimeOffset.UnixEpoch, message);

    private static (CallLlmActivity Activity, CallLlmActivityTests.CapturingChatClient Client) BuildActivity(
        IConversationStore? store, string? instructions = null, ReasoningReplay? replay = null)
    {
        var sp = new EmptyServiceProvider();
        var client = new CallLlmActivityTests.CapturingChatClient();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, client, instructions: instructions, tools: null);
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp,
            NullLogger<CallLlmActivity>.Instance,
            reasoningReplays: replay is { } r ? [new ReasoningReplayRegistration(null, r)] : null,
            conversationStore: store);
        return (activity, client);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class RecordingStore(params ConversationEntry[] entries) : IConversationStore
    {
        public (string Id, long? UpTo, string? Incarnation)? LastLoad { get; private set; }

        public ConversationTurn? LastAppend { get; private set; }

        public Task<ConversationSnapshot> LoadAsync(string conversationId, long? upToSequence = null, string? incarnation = null,
            CancellationToken cancellationToken = default)
        {
            LastLoad = (conversationId, upToSequence, incarnation);
            return Task.FromResult(new ConversationSnapshot(entries.Length, entries, entries.Length > 0 ? "inc-1" : null));
        }

        public Task<ConversationAppendResult> AppendTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
        {
            LastAppend = turn;
            return Task.FromResult(new ConversationAppendResult(ConversationAppendStatus.Appended, 0, turn.Messages.Count, false));
        }

        public Task<ConversationAppendResult> AppendSummaryAsync(ConversationSummary summary, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationEntry>> LoadEntriesAsync(string conversationId, long fromSequence, long? toSequence = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string conversationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
