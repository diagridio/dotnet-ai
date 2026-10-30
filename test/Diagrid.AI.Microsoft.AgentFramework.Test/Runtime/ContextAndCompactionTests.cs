// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies <c>WithContextWindow</c> (what each LLM call sends is fitted to the model) and
/// <c>WithConversationCompaction</c> (older turns of a stored conversation are summarized into the store).
/// </summary>
public sealed class ContextAndCompactionTests
{
    private const string AgentName = "compaction-agent";
    private const string ConversationId = "conv-1";

    // ── WithContextWindow ──────────────────────────────────────────────────

    [Fact]
    public async Task ContextWindow_DropsTheOldestMessages_KeepingTheSystemMessageAndTheLatest()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");
        var history = Enumerable.Range(0, 40)
            .Select(i => new WorkflowChatMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"message {i} " + new string('x', 200) })
            .Append(new WorkflowChatMessage { Role = "user", Content = "latest" })
            .ToList();

        // The 40 earlier messages are history; the run is the latest user message.
        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null, history) { RunMessagesStart = 40 });

        var sent = client.LastMessages!;
        Assert.True(sent.Count < history.Count + 1, $"sent {sent.Count} messages");
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Equal("latest", sent[^1].Text);
    }

    [Fact]
    public async Task ContextWindow_LeavesMessagesThatFitAlone()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 100_000, 1_000)]);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null,
            [new WorkflowChatMessage { Role = "user", Content = "a" }, new WorkflowChatMessage { Role = "assistant", Content = "b" }, new WorkflowChatMessage { Role = "user", Content = "c" }]));

        Assert.Equal(["be brief", "a", "b", "c"], client.LastMessages!.Select(m => m.Text));
    }

    [Fact]
    public async Task ContextWindow_DropsWholeTurns_EvenWhenTheLatestEarlierTurnIsTheLargest()
    {
        // Review regression: MAF's strategy kept the most recent message groups whatever their size, so this sent
        // about six times the window and opened the history with an assistant tool call.
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 4000, 500)]);
        client.SetNextResponse(finalText: "ok");
        var bigResult = "\"" + new string('r', 100_000) + "\"";
        List<WorkflowChatMessage> history =
        [
            new() { Role = "user", Content = "task" },
            new() { Role = "assistant", FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "tool" }] },
            new() { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = "c1", Name = "tool", ResultJson = bigResult }] },
            new() { Role = "assistant", FunctionCalls = [new WorkflowFunctionCall { CallId = "c2", Name = "tool" }] },
            new() { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = "c2", Name = "tool", ResultJson = bigResult }] },
            new() { Role = "assistant", Content = "done" },
            new() { Role = "user", Content = "small question" },
            new() { Role = "assistant", Content = "small answer" },
        ];

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, new WorkflowChatMessage { Role = "user", Content = "next" }]) { RunMessagesStart = history.Count });

        Assert.Equal(["be brief", "small question", "small answer", "next"], client.LastMessages!.Select(m => m.Text));
    }

    [Fact]
    public async Task ContextWindow_SendsTheHistoryStartingAtAUserMessage()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");
        var history = Enumerable.Range(0, 30)
            .SelectMany(i => new[]
            {
                new WorkflowChatMessage { Role = "user", Content = $"question {i} " + new string('q', 100) },
                new WorkflowChatMessage { Role = "assistant", FunctionCalls = [new WorkflowFunctionCall { CallId = $"c{i}", Name = "tool" }] },
                new WorkflowChatMessage { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = $"c{i}", Name = "tool", ResultJson = "\"" + new string('r', 100) + "\"" }] },
                new WorkflowChatMessage { Role = "assistant", Content = $"answer {i}" },
            })
            .ToList();

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, new WorkflowChatMessage { Role = "user", Content = "latest" }]) { RunMessagesStart = history.Count });

        var sent = client.LastMessages!;
        Assert.InRange(sent.Count, 3, history.Count);
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Equal(ChatRole.User, sent[1].Role);
        Assert.StartsWith("question", sent[1].Text);
        Assert.Equal(0, (sent.Count - 2) % 4);
    }

    [Fact]
    public async Task ContextWindow_ARunThatAloneDoesNotFit_FailsWithoutCallingTheModel()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 1000, 200)]);
        client.SetNextResponse(finalText: "ok");
        List<WorkflowChatMessage> run =
        [
            new() { Role = "user", Content = "task" },
            new() { Role = "assistant", FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "tool" }] },
            new() { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = "c1", Name = "tool", ResultJson = "\"" + new string('r', 8000) + "\"" }] },
        ];

        var ex = await Assert.ThrowsAsync<ContextWindowExceededException>(() =>
            activity.RunAsync(new TestWorkflowActivityContext("i"), new CallLlmInput(AgentName, null, run)));

        Assert.Contains(AgentName, ex.Message);
        Assert.Contains("1000", ex.Message);
        Assert.Null(client.LastMessages);
    }

    [Fact]
    public async Task ContextWindow_KeepsTheContextProvidersMessages()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");
        var history = Enumerable.Range(0, 20)
            .Select(i => new WorkflowChatMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"old {i} " + new string('h', 200) })
            .ToList();

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, new WorkflowChatMessage { Role = "user", Content = "latest" }])
            {
                RunMessagesStart = history.Count,
                AdditionalMessages = [new WorkflowChatMessage { Role = "user", Content = "retrieved memory" }],
            });

        var sent = client.LastMessages!;
        Assert.Equal("retrieved memory", sent[1].Text);
        Assert.Equal("latest", sent[^1].Text);
        Assert.True(sent.Count < history.Count + 3, $"sent {sent.Count} messages");
    }

    [Fact]
    public void SplitTurns_StartsATurnAtEachUserMessage_AndKeepsALeadingSummaryApart()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "instructions"),
            new(ChatRole.System, "summary"),
            new(ChatRole.User, "a"),
            new(ChatRole.Assistant, "b"),
            new(ChatRole.Tool, "r"),
            new(ChatRole.User, "c"),
            new(ChatRole.User, "the run"),
        ];

        Assert.Equal([[1], [2, 3, 4], [5]], ContextWindowFitter.SplitTurnsByUserMessage(messages, 1, 6));
    }

    [Fact]
    public void Fitter_DropsWholeEarlierTurnsOldestFirst_AndKeepsTheHeadAndTheRun()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "instructions"),
            new(ChatRole.User, "old question " + new string('a', 400)),
            new(ChatRole.Assistant, "old answer " + new string('b', 400)),
            new(ChatRole.User, "recent question"),
            new(ChatRole.Assistant, "recent answer"),
            new(ChatRole.User, "the run"),
        ];

        var fit = ContextWindowFitter.Fit(new PromptLayout(messages, HeadCount: 1, HistoryCount: 4, HistoryStartsWithSummary: false), 150, 50);

        Assert.Equal(2, fit.Dropped);
        Assert.Equal(["instructions", "recent question", "recent answer", "the run"], fit.Messages!.Select(m => m.Text));

        var tooSmall = ContextWindowFitter.Fit(new PromptLayout(messages, 1, 4, false), 3, 1);
        Assert.Null(tooSmall.Messages);
        Assert.True(tooSmall.RequiredTokens > 2);
    }

    [Fact]
    public async Task ContextWindow_ASystemContextMessage_IsCountedOnce()
    {
        // Budget 300; an 800-character system context message is about 200 tokens, so the call fits.
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "latest" }])
            {
                AdditionalMessages = [new WorkflowChatMessage { Role = "system", Content = new string('m', 800) }],
            });

        Assert.Equal("latest", client.LastMessages![^1].Text);
    }

    [Fact]
    public async Task ContextWindow_KeepsACallersSystemMessageInTheHistory()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");
        List<WorkflowChatMessage> history =
        [
            new() { Role = "system", Content = "PERSONA: answer in French" },
            .. Enumerable.Range(0, 10).Select(i => new WorkflowChatMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"old {i} " + new string('h', 200) }),
        ];

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, new WorkflowChatMessage { Role = "user", Content = "latest" }]) { RunMessagesStart = history.Count });

        var sent = client.LastMessages!;
        Assert.Contains(sent, m => m.Text == "PERSONA: answer in French");
        Assert.True(sent.Count < history.Count + 2, $"sent {sent.Count} messages");
    }

    [Fact]
    public async Task ContextWindow_ReasoningThatIsNotSent_DoesNotCount()
    {
        // Another agent's reasoning sits in the history; this agent never sends it, so it must not push turns out.
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 400, 100)]);
        client.SetNextResponse(finalText: "ok");
        List<WorkflowChatMessage> history =
        [
            new() { Role = "user", Content = "question" },
            new()
            {
                Role = "assistant",
                Content = "answer",
                Reasoning = [new WorkflowReasoningContent { Text = new string('x', 4000), ProtectedData = new string('p', 4000), Source = "another-agent" }],
            },
        ];

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, new WorkflowChatMessage { Role = "user", Content = "latest" }]) { RunMessagesStart = history.Count });

        Assert.Equal(["be brief", "question", "answer", "latest"], client.LastMessages!.Select(m => m.Text));
    }

    [Fact]
    public async Task ContextWindow_AStoredSummaryCanBeLeftOut_LikeAnyTurn()
    {
        var store = await NewStoreWithTurns(1, padding: 600);
        var inc = (await store.LoadAsync(ConversationId)).Incarnation;
        await store.AppendSummaryAsync(new ConversationSummary(ConversationId, "s1", 2, AgentName, null,
            new WorkflowChatMessage { Role = "system", Content = "SUMMARY " + new string('s', 1200) }, inc));
        await AppendTurn(store, "t1", "recent question", "recent answer");
        var (activity, client) = BuildActivity(store: store, contextWindows: [new ContextWindowRegistration(null, 300, 100)]);
        client.SetNextResponse(finalText: "ok");

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "latest" }]) { ConversationId = ConversationId });

        Assert.Equal(["be brief", "recent question", "recent answer", "latest"], client.LastMessages!.Select(m => m.Text));
    }

    [Fact]
    public void ContextWindow_NamedRegistrationOverridesTheDefault()
    {
        ContextWindowRegistration[] registrations =
        [
            new(null, 1000, 100),
            new(AgentName.ToUpperInvariant(), 5000, 500),
        ];

        Assert.Equal(5000, ContextWindowRegistration.Resolve(registrations, AgentName)!.MaxContextTokens);
        Assert.Equal(1000, ContextWindowRegistration.Resolve(registrations, "other")!.MaxContextTokens);
        Assert.Null(ContextWindowRegistration.Resolve([], AgentName));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(100, -1)]
    public void WithContextWindow_RejectsInvalidSizes(int maxContext, int maxOutput)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithContextWindow(maxContext, maxOutput));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithContextWindow(AgentName, maxContext, maxOutput));
    }

    // ── Compaction is flagged by the first LLM call ────────────────────────

    [Theory]
    [InlineData(10, true)]
    [InlineData(100_000, false)]
    public async Task CallLlm_FlagsCompactionWhenTheLoadedHistoryIsPastTheTrigger(int triggerTokens, bool due)
    {
        var store = await NewStoreWithTurns(3);
        var (activity, client) = BuildActivity(store: store,
            compactions: [new ConversationCompactionRegistration(null, new ConversationCompactionOptions { TriggerTokens = triggerTokens })]);
        client.SetNextResponse(finalText: "ok");

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "now" }]) { ConversationId = ConversationId });

        Assert.Equal(due ? true : null, output.CompactionDue);
    }

    [Fact]
    public async Task CallLlm_NeverFlagsCompactionWithoutAConversationOrASetting()
    {
        var store = await NewStoreWithTurns(3);
        var (withoutSetting, client) = BuildActivity(store: store);
        client.SetNextResponse(finalText: "ok");
        var output = await withoutSetting.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "now" }]) { ConversationId = ConversationId });
        Assert.Null(output.CompactionDue);

        var (withoutConversation, client2) = BuildActivity(store: store,
            compactions: [new ConversationCompactionRegistration(null, new ConversationCompactionOptions { TriggerTokens = 1 })]);
        client2.SetNextResponse(finalText: "ok");
        output = await withoutConversation.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = new string('x', 400) }]));
        Assert.Null(output.CompactionDue);
    }

    // ── The workflow schedules it ──────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workflow_CompactsAfterTheCommit_OnlyWhenTheFirstCallFlaggedIt(bool due)
    {
        var context = new TestWorkflowContext("wf-1", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
            nameof(AppendConversationTurnActivity) => Task.FromResult<object?>(new ConversationAppendResult(ConversationAppendStatus.Appended, 4, 6, false)),
            nameof(CompactConversationActivity) => Task.FromResult<object?>(new CompactConversationOutput { Compacted = true }),
            _ => Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "done", HistorySequence = 4, HistoryIncarnation = "inc", CompactionDue = due ? true : null }),
        });

        await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "hi", null, null) { ChatClientKey = "key", ConversationId = ConversationId });

        var names = context.ActivityCalls.Select(c => c.Name).ToList();
        if (!due)
        {
            Assert.DoesNotContain(nameof(CompactConversationActivity), names);
            return;
        }

        Assert.Equal(
            [nameof(ResolveAgentContextActivity), nameof(CallLlmActivity), nameof(AppendConversationTurnActivity), nameof(CompactConversationActivity), nameof(CompleteAgentContextActivity)],
            names);
        var append = (AppendConversationTurnInput)context.ActivityCalls[2].Input!;
        var compact = (CompactConversationInput)context.ActivityCalls[3].Input!;
        Assert.Equal(new CompactConversationInput(ConversationId, append.TurnId + ":summary", AgentName, "key", "inc"), compact);
    }

    // ── CompactConversationActivity ────────────────────────────────────────

    // Each turn below is about 105 estimated tokens. With a 300-token trigger, half of it (150) keeps one turn.
    private const int Padding = 200;

    [Fact]
    public async Task Compaction_SummarizesTheOlderTurns_AndLaterLoadsUseTheSummary()
    {
        var store = await NewStoreWithTurns(5, Padding);
        var summarizer = new SummarizingClient("S1");
        var activity = BuildCompaction(store, new ConversationCompactionOptions
        {
            TriggerTokens = 300, KeepRecentTurns = 2, MaxSummaryInputTokens = 1000, ChatClient = summarizer,
        });

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CompactConversationInput(ConversationId, "wf:1:summary", AgentName, null, null));

        Assert.True(output.Compacted);
        Assert.Equal(8, output.CoversUpToSequence);
        var transcript = summarizer.Requests.Single()[1].Text;
        Assert.Contains("user: question 0", transcript, StringComparison.Ordinal);
        Assert.Contains("assistant: answer 3", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("question 4", transcript, StringComparison.Ordinal);
        Assert.Equal(ConversationCompactionOptions.DefaultSummarizationPrompt, summarizer.Requests.Single()[0].Text);

        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"{CompactConversationActivity.SummaryHeader}\nS1", loaded.Entries[0].Message.Content);
        Assert.Equal("system", loaded.Entries[0].Message.Role);
        Assert.Equal(["question 4", "answer 4"], loaded.Entries.Skip(1).Select(e => e.Message.Content![..^Padding]));
    }

    [Fact]
    public async Task Compaction_ASecondTime_IncludesTheEarlierSummary()
    {
        var options = new ConversationCompactionOptions { TriggerTokens = 300, KeepRecentTurns = 2, MaxSummaryInputTokens = 1000 };
        var store = await NewStoreWithTurns(4, Padding);
        options.ChatClient = new SummarizingClient("S1");
        await BuildCompaction(store, options).RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "a:summary", AgentName, null, null));
        await AppendTurn(store, "t4", "question 4" + new string('.', Padding), "answer 4" + new string('.', Padding));
        await AppendTurn(store, "t5", "question 5" + new string('.', Padding), "answer 5" + new string('.', Padding));

        var summarizer = new SummarizingClient("S2");
        options.ChatClient = summarizer;
        var output = await BuildCompaction(store, options).RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "b:summary", AgentName, null, null));

        Assert.True(output.Compacted);
        var transcript = summarizer.Requests.Single()[1].Text;
        Assert.StartsWith($"Earlier summary:\n{CompactConversationActivity.SummaryHeader}\nS1", transcript.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("question 3", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("question 1", transcript, StringComparison.Ordinal);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"{CompactConversationActivity.SummaryHeader}\nS2", loaded.Entries[0].Message.Content);
        Assert.Equal(["question 5", "answer 5"], loaded.Entries.Skip(1).Select(e => e.Message.Content![..^Padding]));
    }

    [Fact]
    public async Task Compaction_DoesNothing_BelowTheTrigger()
    {
        var store = await NewStoreWithTurns(4);
        var summarizer = new SummarizingClient("S");
        var activity = BuildCompaction(store, new ConversationCompactionOptions { TriggerTokens = 100_000, ChatClient = summarizer });

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        Assert.False(output.Compacted);
        Assert.Null(output.Error);
        Assert.Empty(summarizer.Requests);
    }

    [Fact]
    public async Task Compaction_ThatFails_ReportsItWithoutThrowing()
    {
        var store = await NewStoreWithTurns(4);
        var activity = BuildCompaction(store, new ConversationCompactionOptions { TriggerTokens = 1, KeepRecentTurns = 1, ChatClient = new SummarizingClient(null) });

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        Assert.False(output.Compacted);
        Assert.Contains("summarizer down", output.Error, StringComparison.Ordinal);
        Assert.All((await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken)).Entries, e => Assert.Null(e.SummaryOf));
    }

    [Fact]
    public async Task Compaction_UsesTheAgentsOwnClient_WhenNoneIsSet()
    {
        var store = await NewStoreWithTurns(3);
        var agentClient = new SummarizingClient("from agent");
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, agentClient, instructions: null, tools: null);
        var sp = new EmptyServiceProvider();
        var activity = new CompactConversationActivity(registry, new AgentRegistry(sp, []), sp, NullLogger<CompactConversationActivity>.Instance,
            [new ConversationCompactionRegistration(null, new ConversationCompactionOptions { TriggerTokens = 1, KeepRecentTurns = 1 })],
            conversationStore: store);

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        Assert.True(output.Compacted);
        Assert.Single(agentClient.Requests);
    }

    [Theory]
    [InlineData(0, 4, "x")]
    [InlineData(10, 0, "x")]
    [InlineData(10, 4, " ")]
    public void WithConversationCompaction_RejectsInvalidOptions(int triggerTokens, int keepRecentTurns, string prompt)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithConversationCompaction(o =>
        {
            o.TriggerTokens = triggerTokens;
            o.KeepRecentTurns = keepRecentTurns;
            o.SummarizationPrompt = prompt;
        }));
    }

    [Fact]
    public void SplitTurns_GroupsConsecutiveEntriesOfOneTurn()
    {
        static ConversationEntry E(long seq, string turn) => new(seq, turn, AgentName, null, DateTimeOffset.UnixEpoch, new WorkflowChatMessage { Role = "user" });

        var turns = CompactionPlanner.SplitTurnsByTurnId([E(0, "a"), E(1, "a"), E(2, "b"), E(3, "c"), E(4, "c")]);

        Assert.Equal([2, 1, 2], turns.Select(t => t.Count));
    }

    // ── Review regressions ─────────────────────────────────────────────────

    [Fact]
    public async Task ContextWindow_NeverTrimsTheRunsOwnMessages_EvenInALongToolLoop()
    {
        var (activity, client) = BuildActivity(contextWindows: [new ContextWindowRegistration(null, 3000, 500)]);
        client.SetNextResponse(finalText: "ok");
        var history = Enumerable.Range(0, 20)
            .Select(i => new WorkflowChatMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"old {i} " + new string('h', 600) })
            .ToList();
        var run = new List<WorkflowChatMessage> { new() { Role = "user", Content = "CURRENT TASK" } };
        for (var i = 0; i < 9; i++)
        {
            run.Add(new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = $"c{i}", Name = "tool" }],
                Reasoning = [new WorkflowReasoningContent { Text = $"thinking {i}", ProtectedData = $"sig {i}", Source = AgentName }],
            });
            run.Add(new WorkflowChatMessage { Role = "tool", FunctionResults = [new WorkflowFunctionResult { CallId = $"c{i}", Name = "tool", ResultJson = "\"" + new string('r', 400) + "\"" }] });
        }

        await activity.RunAsync(new TestWorkflowActivityContext("i"),
            new CallLlmInput(AgentName, null, [.. history, .. run]) { RunMessagesStart = history.Count });

        var sent = client.LastMessages!;
        // Every run message is sent unchanged, in order, at the end; only the earlier turns were trimmed.
        Assert.True(sent.Count < 1 + history.Count + run.Count, $"sent {sent.Count} messages");
        var tail = sent.TakeLast(run.Count).ToList();
        Assert.Equal("CURRENT TASK", tail[0].Text);
        Assert.Equal(9, tail.Count(m => m.Contents.OfType<TextReasoningContent>().Any()));
        Assert.Equal(9, tail.Count(m => m.Contents.OfType<FunctionCallContent>().Any()));
        Assert.Equal(ChatRole.System, sent[0].Role);
    }

    [Fact]
    public async Task Compaction_SummarizesOnlyWhatFitsTheSummaryBudget_AndLaterRunsContinue()
    {
        var store = await NewStoreWithTurns(6, padding: 200);
        var summarizer = new SummarizingClient("S1");
        var activity = BuildCompaction(store, new ConversationCompactionOptions
        {
            TriggerTokens = 1, KeepRecentTurns = 1, MaxSummaryInputTokens = 150, ChatClient = summarizer,
        });

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        // Each turn is about 110 tokens, so only the first fits: the summary covers up to the second turn.
        Assert.Equal(2, output.CoversUpToSequence);
        var transcript = summarizer.Requests.Single()[1].Text;
        Assert.Contains("question 0", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("question 1", transcript, StringComparison.Ordinal);
        var loaded = await store.LoadAsync(ConversationId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("question 1", loaded.Entries[1].Message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compaction_ATurnLargerThanTheBudget_IsCut()
    {
        var store = await NewStoreWithTurns(3, padding: 4000);
        var summarizer = new SummarizingClient("S1");
        var activity = BuildCompaction(store, new ConversationCompactionOptions
        {
            TriggerTokens = 1, KeepRecentTurns = 1, MaxSummaryInputTokens = 100, ChatClient = summarizer,
        });

        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        var transcript = summarizer.Requests.Single()[1].Text;
        Assert.True(transcript.Length <= 100 * 4 + 20, $"{transcript.Length} characters");
        Assert.EndsWith("[truncated]", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanCompaction_KeepsFewerRecentTurnsWhenTheyAreLarge_ButAlwaysTheLatest()
    {
        static List<ConversationEntry> Turn(string id, long seq, int size) =>
            [new(seq, id, AgentName, null, DateTimeOffset.UnixEpoch, new WorkflowChatMessage { Role = "user", Content = new string('x', size * 4) })];
        var turns = new List<List<ConversationEntry>> { Turn("a", 0, 10), Turn("b", 1, 10), Turn("c", 2, 300), Turn("d", 3, 300) };

        var plan = CompactionPlanner.PlanCompaction(turns, null,
            new ConversationCompactionOptions { TriggerTokens = 400, KeepRecentTurns = 4 });

        // Half the trigger is 200 tokens: only the latest 300-token turn is kept.
        Assert.Equal(3, plan!.Value.CoversUpTo);
        Assert.Null(CompactionPlanner.PlanCompaction([Turn("only", 0, 10)], null,
            new ConversationCompactionOptions { TriggerTokens = 1, KeepRecentTurns = 1 }));
    }

    [Fact]
    public async Task Compaction_WithTheAgentsClient_SendsTheAgentsOptionsWithoutTools()
    {
        var store = await NewStoreWithTurns(3);
        var agentClient = new SummarizingClient("from agent");
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, agentClient, instructions: null,
            tools: [AIFunctionFactory.Create(() => "x", "tool")], contextProviders: null,
            baseOptions: new ChatOptions { ModelId = "agent-model", Temperature = 0.3f, ToolMode = ChatToolMode.RequireAny });
        var sp = new EmptyServiceProvider();
        var activity = new CompactConversationActivity(registry, new AgentRegistry(sp, []), sp, NullLogger<CompactConversationActivity>.Instance,
            [new ConversationCompactionRegistration(null, new ConversationCompactionOptions { TriggerTokens = 1, KeepRecentTurns = 1 })],
            conversationStore: store);

        await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, null));

        var options = agentClient.Options.Single()!;
        Assert.Equal(("agent-model", 0.3f), (options.ModelId, options.Temperature));
        Assert.Null(options.Tools);
        Assert.Null(options.ToolMode);
    }

    [Fact]
    public async Task Compaction_OfAConversationCreatedAgain_IsSkipped()
    {
        var store = await NewStoreWithTurns(3);
        var oldIncarnation = (await store.LoadAsync(ConversationId)).Incarnation;
        await store.DeleteAsync(ConversationId);
        await AppendTurn(store, "n0", "new 0", "a");
        await AppendTurn(store, "n1", "new 1", "a");
        var summarizer = new SummarizingClient("S");
        var activity = BuildCompaction(store, new ConversationCompactionOptions { TriggerTokens = 1, KeepRecentTurns = 1, ChatClient = summarizer });

        var output = await activity.RunAsync(new TestWorkflowActivityContext("i"), new CompactConversationInput(ConversationId, "s", AgentName, null, oldIncarnation));

        Assert.False(output.Compacted);
        Assert.NotNull(output.Error);
        Assert.Empty(summarizer.Requests);
    }

    [Fact]
    public async Task Workflow_TakesCompactionDueFromTheFirstCallOnly_AndCompactsOnTheMaxIterationsPath()
    {
        static TestWorkflowContext Context(Func<int, CallLlmOutput> llm)
        {
            var call = 0;
            return new TestWorkflowContext("wf-1", (name, _) => name switch
            {
                nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
                nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
                nameof(ExecuteToolActivity) => Task.FromResult<object?>(new ExecuteToolOutput { CallId = "c", FunctionName = "tool", ResultJson = "1" }),
                nameof(AppendConversationTurnActivity) => Task.FromResult<object?>(new ConversationAppendResult(ConversationAppendStatus.Appended, 4, 6, false)),
                nameof(CompactConversationActivity) => Task.FromResult<object?>(new CompactConversationOutput()),
                _ => Task.FromResult<object?>(llm(call++)),
            });
        }

        static CallLlmOutput ToolCall(bool? due) => new() { FunctionCalls = [new WorkflowFunctionCall { CallId = "c", Name = "tool" }], HistorySequence = 4, CompactionDue = due };

        // Only a later call reports it due: no compaction.
        var later = Context(i => i == 0 ? ToolCall(null) : new CallLlmOutput { IsFinal = true, Text = "x", CompactionDue = true });
        await new AgentRunWorkflow().RunAsync(later, new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId });
        Assert.DoesNotContain(nameof(CompactConversationActivity), later.ActivityCalls.Select(c => c.Name));

        // The run hits its iteration limit after the first call reported it due: compaction after the commit.
        var maxed = Context(_ => ToolCall(true));
        await new AgentRunWorkflow().RunAsync(maxed, new DaprAgentInvocation(AgentName, "hi", null, null) { ConversationId = ConversationId });
        Assert.Equal([nameof(AppendConversationTurnActivity), nameof(CompactConversationActivity), nameof(CompleteAgentContextActivity)],
            maxed.ActivityCalls.Select(c => c.Name).TakeLast(3));
    }

    [Fact]
    public void Builder_RegistrationsReachTheActivities()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents()
            .WithContextWindow(8000, 1000)
            .WithContextWindow(AgentName, 16000, 2000)
            .WithConversationCompaction(AgentName, o => o.TriggerTokens = 100);
        var provider = services.BuildServiceProvider();

        var windows = provider.GetServices<ContextWindowRegistration>().ToList();
        Assert.Equal(16000, ContextWindowRegistration.Resolve(windows, AgentName)!.MaxContextTokens);
        Assert.Equal(100, ConversationCompactionRegistration.Resolve(provider.GetServices<ConversationCompactionRegistration>(), AgentName)!.Options.TriggerTokens);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task<DaprConversationStore> NewStoreWithTurns(int turns, int padding = 0)
    {
        var store = new DaprConversationStore(new InMemoryConversationStateBackend(),
            new DaprConversationStoreOptions { StateStoreName = "statestore" }, TimeProvider.System)
        { Delay = (_, _) => Task.CompletedTask };
        for (var i = 0; i < turns; i++)
        {
            await AppendTurn(store, $"t{i}", $"question {i}" + new string('.', padding), $"answer {i}" + new string('.', padding));
        }

        return store;
    }

    private static async Task AppendTurn(DaprConversationStore store, string turnId, string question, string answer)
    {
        var next = (await store.LoadAsync(ConversationId)).NextSequence;
        await store.AppendTurnAsync(new ConversationTurn(ConversationId, turnId, next, AgentName, null,
            [new WorkflowChatMessage { Role = "user", Content = question }, new WorkflowChatMessage { Role = "assistant", Content = answer }]));
    }

    private static CompactConversationActivity BuildCompaction(IConversationStore store, ConversationCompactionOptions options)
    {
        var sp = new EmptyServiceProvider();
        return new CompactConversationActivity(new ChatClientRegistry(), new AgentRegistry(sp, []), sp,
            NullLogger<CompactConversationActivity>.Instance,
            [new ConversationCompactionRegistration(null, options)], conversationStore: store);
    }

    private static (CallLlmActivity Activity, CallLlmActivityTests.CapturingChatClient Client) BuildActivity(
        IConversationStore? store = null,
        IEnumerable<ContextWindowRegistration>? contextWindows = null,
        IEnumerable<ConversationCompactionRegistration>? compactions = null)
    {
        var sp = new EmptyServiceProvider();
        var client = new CallLlmActivityTests.CapturingChatClient();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, client, instructions: "be brief", tools: null);
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp,
            NullLogger<CallLlmActivity>.Instance, conversationStore: store, contextWindows: contextWindows, compactions: compactions);
        return (activity, client);
    }

    /// <summary>Answers every request with a fixed summary, or fails when it has none.</summary>
    private sealed class SummarizingClient(string? summary) : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            Options.Add(options);
            return summary is null
                ? throw new InvalidOperationException("summarizer down")
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, summary)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
