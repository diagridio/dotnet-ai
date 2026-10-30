// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies the reasoning effort (<c>WithReasoning</c>), the agent's own <see cref="ChatOptions"/>
/// being applied to every LLM call, and how much earlier reasoning is sent back and kept
/// (<c>WithReasoningReplay</c>).
/// </summary>
public sealed class ReasoningOptionsTests
{
    private const string AgentName = "reasoning-options-agent";

    // ── Effort resolution ──────────────────────────────────────────────────

    [Fact]
    public void ResolveEffort_NamedBeatsAgentOptionsBeatsDefault()
    {
        var agentOptions = new ReasoningOptions { Effort = ReasoningEffort.Medium };
        var registrations = new[]
        {
            new ReasoningEffortRegistration(null, ReasoningEffort.Low, null),
            new ReasoningEffortRegistration(AgentName.ToUpperInvariant(), ReasoningEffort.High, ReasoningOutput.Full)
        };

        Assert.Equal(ReasoningEffort.High, ReasoningEffortRegistration.Resolve(registrations, AgentName, agentOptions)!.Effort);
        Assert.Equal(ReasoningEffort.Medium, ReasoningEffortRegistration.Resolve(registrations[..1], AgentName, agentOptions)!.Effort);
        Assert.Equal(ReasoningEffort.Low, ReasoningEffortRegistration.Resolve(registrations[..1], AgentName, null)!.Effort);
        Assert.Null(ReasoningEffortRegistration.Resolve([], AgentName, null));
    }

    [Fact]
    public void ResolveEffort_TakesEffortAndOutputEachFromTheirOwnSource()
    {
        // The agent sets only the output, the default only the effort: the call gets both.
        var agentOptions = new ReasoningOptions { Output = ReasoningOutput.Summary };
        var registrations = new[] { new ReasoningEffortRegistration(null, ReasoningEffort.Low, null) };

        var resolved = ReasoningEffortRegistration.Resolve(registrations, AgentName, agentOptions)!;

        Assert.Equal(ReasoningEffort.Low, resolved.Effort);
        Assert.Equal(ReasoningOutput.Summary, resolved.Output);
    }

    [Fact]
    public void ResolveReplay_NamedBeatsDefault_AndDefaultsToAcrossTurns()
    {
        var registrations = new[]
        {
            new ReasoningReplayRegistration(null, ReasoningReplay.WithinRun),
            new ReasoningReplayRegistration(AgentName, ReasoningReplay.Off)
        };

        Assert.Equal(ReasoningReplay.Off, ReasoningReplayRegistration.Resolve(registrations, AgentName));
        Assert.Equal(ReasoningReplay.WithinRun, ReasoningReplayRegistration.Resolve(registrations, "other-agent"));
        Assert.Equal(ReasoningReplay.AcrossTurns, ReasoningReplayRegistration.Resolve([], AgentName));
    }

    // ── ChatOptions sent to the chat client ────────────────────────────────

    [Fact]
    public async Task CallLlm_WithDefaultEffort_SendsIt()
    {
        var (activity, client) = BuildActivity(efforts: [new ReasoningEffortRegistration(null, ReasoningEffort.High, null)]);

        await activity.RunAsync(MakeContext(), UserInput());

        Assert.Equal(ReasoningEffort.High, client.LastOptions!.Reasoning!.Effort);
    }

    [Fact]
    public async Task CallLlm_AgentOptions_AreApplied_AndNamedEffortOverridesTheirReasoning()
    {
        var baseOptions = new ChatOptions
        {
            Temperature = 0.2f,
            MaxOutputTokens = 512,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low }
        };
        var (activity, client) = BuildActivity(baseOptions,
            efforts: [new ReasoningEffortRegistration(AgentName, ReasoningEffort.ExtraHigh, null)]);

        await activity.RunAsync(MakeContext(), UserInput());

        Assert.Equal(0.2f, client.LastOptions!.Temperature);
        Assert.Equal(512, client.LastOptions.MaxOutputTokens);
        Assert.Equal(ReasoningEffort.ExtraHigh, client.LastOptions.Reasoning!.Effort);
        // The registered options are shared by every call, so they must not change.
        Assert.Equal(ReasoningEffort.Low, baseOptions.Reasoning.Effort);
    }

    [Fact]
    public async Task CallLlm_AgentReasoning_IsUsedWhenNothingIsRegistered()
    {
        var (activity, client) = BuildActivity(new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } });

        await activity.RunAsync(MakeContext(), UserInput());

        Assert.Equal(ReasoningEffort.Medium, client.LastOptions!.Reasoning!.Effort);
    }

    [Fact]
    public async Task CallLlm_RunOptions_OverrideOnlyWhatTheySet()
    {
        var agentFormat = ChatResponseFormat.Json;
        var baseOptions = new ChatOptions
        {
            ResponseFormat = agentFormat,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["agent"] = "a", ["shared"] = "agent" }
        };
        var (activity, client) = BuildActivity(baseOptions);
        var runOptions = new AgentRunOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["run"] = "r", ["shared"] = "run" }
        };

        await activity.RunAsync(MakeContext(), UserInput() with { Options = runOptions });

        Assert.Same(agentFormat, client.LastOptions!.ResponseFormat);
        Assert.Equal("a", client.LastOptions.AdditionalProperties!["agent"]);
        Assert.Equal("r", client.LastOptions.AdditionalProperties["run"]);
        Assert.Equal("run", client.LastOptions.AdditionalProperties["shared"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallLlm_RequiredToolMode_IsSentOnlyOnTheRunsFirstCall(bool requireSpecific)
    {
        // Sent on every call, a required tool mode would make the model call tools until the run
        // hits its iteration limit; FunctionInvokingChatClient drops it after the first round too.
        var toolMode = requireSpecific ? ChatToolMode.RequireSpecific("lookup") : ChatToolMode.RequireAny;
        var (activity, client) = BuildActivity(new ChatOptions { ToolMode = toolMode });

        await activity.RunAsync(MakeContext(), UserInput() with { Iteration = 0 });
        var first = client.LastOptions!.ToolMode;
        await activity.RunAsync(MakeContext(), UserInput() with { Iteration = 1 });

        Assert.IsAssignableFrom<RequiredChatToolMode>(first);
        Assert.Null(client.LastOptions!.ToolMode);
    }

    [Fact]
    public async Task CallLlm_AutoToolMode_IsKeptOnFollowUpCalls()
    {
        var (activity, client) = BuildActivity(new ChatOptions { ToolMode = ChatToolMode.Auto });

        await activity.RunAsync(MakeContext(), UserInput() with { Iteration = 3 });

        Assert.Same(ChatToolMode.Auto, client.LastOptions!.ToolMode);
    }

    [Fact]
    public async Task CallLlm_NothingConfigured_SendsNoOptions()
    {
        var (activity, client) = BuildActivity();

        await activity.RunAsync(MakeContext(), UserInput());

        Assert.Null(client.LastOptions);
    }

    [Fact]
    public void RegisterAgentComponents_KeepsAgentOptions_WithoutToolsInstructionsOrConversationId()
    {
        var services = new ServiceCollection().AddSingleton<ChatClientRegistry>().AddSingleton<ToolRegistry>().BuildServiceProvider();
        var tool = AIFunctionFactory.Create(() => "ok", "lookup");
        var agent = new CallLlmActivityTests.CapturingChatClient().AsAIAgent(new ChatClientAgentOptions
        {
            Name = AgentName,
            ChatOptions = new ChatOptions
            {
                Instructions = "Be brief.",
                Temperature = 0.3f,
                ConversationId = "server-side",
#pragma warning disable MEAI001
                AllowBackgroundResponses = true,
                ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1 }),
#pragma warning restore MEAI001
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
                Tools = [tool]
            }
        });

        AgentComponentRegistrar.RegisterAgentComponents(services, agent, new CallLlmActivityTests.CapturingChatClient());

        var config = services.GetRequiredService<ChatClientRegistry>().Get(AgentName)!;
        Assert.Equal(0.3f, config.BaseOptions!.Temperature);
        Assert.Equal(ReasoningEffort.High, config.BaseOptions.Reasoning!.Effort);
        Assert.Null(config.BaseOptions.Tools);
        Assert.Null(config.BaseOptions.Instructions);
        Assert.Null(config.BaseOptions.ConversationId);
#pragma warning disable MEAI001
        // The workflow neither polls background responses nor resumes from a token.
        Assert.Null(config.BaseOptions.AllowBackgroundResponses);
        Assert.Null(config.BaseOptions.ContinuationToken);
#pragma warning restore MEAI001
        Assert.Single(config.Tools!);
    }

    [Fact]
    public void WithAgent_NamedFactoryOverload_RegistersTheAgentUnderItsName()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents().WithAgent("named-agent", "conversation-x", chatClient =>
            chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = "factory-name",
                ChatOptions = new ChatOptions { Instructions = "Help.", Temperature = 0.5f }
            }));
        // Stands in for the Dapr chat client the overload registers under the component name.
        services.AddKeyedSingleton<IChatClient>("conversation-x", new CallLlmActivityTests.CapturingChatClient());
        var provider = services.BuildServiceProvider();

        provider.GetServices<AgentFactoryRegistration>().Single(r => r.Name == "named-agent").Factory(provider);

        var config = provider.GetRequiredService<ChatClientRegistry>().Get("named-agent")!;
        Assert.Equal("Help.", config.Instructions);
        Assert.Equal(0.5f, config.BaseOptions!.Temperature);
    }

    // ── Replay: what is sent back ──────────────────────────────────────────

    [Fact]
    public async Task CallLlm_ReplayOff_SendsNoReasoning_AndRecordsTheSetting()
    {
        var (activity, client) = BuildActivity(replays: [new ReasoningReplayRegistration(AgentName, ReasoningReplay.Off)]);

        var output = await activity.RunAsync(MakeContext(), HistoryInput(runMessagesStart: 2));

        Assert.DoesNotContain(client.LastMessages!.SelectMany(m => m.Contents), c => c is TextReasoningContent);
        Assert.Equal(ReasoningReplay.Off, output.ReasoningReplay);
    }

    [Fact]
    public async Task CallLlm_ReplayWithinRun_SendsOnlyThisRunsReasoning()
    {
        var (activity, client) = BuildActivity(replays: [new ReasoningReplayRegistration(null, ReasoningReplay.WithinRun)]);

        await activity.RunAsync(MakeContext(), HistoryInput(runMessagesStart: 2));

        var sent = client.LastMessages!.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(r => r.Text);
        Assert.Equal(["this run"], sent);
    }

    [Fact]
    public async Task CallLlm_ReplayAcrossTurns_SendsAllOfTheAgentsReasoning()
    {
        var (activity, client) = BuildActivity();

        var output = await activity.RunAsync(MakeContext(), HistoryInput(runMessagesStart: 2));

        var sent = client.LastMessages!.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(r => r.Text);
        Assert.Equal(["earlier turn", "this run"], sent);
        Assert.Equal(ReasoningReplay.AcrossTurns, output.ReasoningReplay);
    }

    // ── Replay: what the workflow keeps ────────────────────────────────────

    [Fact]
    public async Task Workflow_ReplayOff_KeepsNoReasoningOnMessages_ButReturnsIt()
    {
        var calls = new List<List<WorkflowChatMessage>>();
        var result = await RunToolLoop(ReasoningReplay.Off, calls);

        Assert.All(result.TurnMessages, m => Assert.Null(m.Reasoning));
        Assert.All(calls[1], m => Assert.Null(m.Reasoning));
        Assert.Contains(result.Response.Messages.Single().Contents, c => c is TextReasoningContent);
    }

    [Fact]
    public async Task Workflow_ReplayWithinRun_KeepsReasoningInTheLoop_ButNotInTurnMessages()
    {
        var calls = new List<List<WorkflowChatMessage>>();
        var result = await RunToolLoop(ReasoningReplay.WithinRun, calls);

        Assert.Contains(calls[1], m => m.Reasoning is { Count: > 0 });
        Assert.All(result.TurnMessages, m => Assert.Null(m.Reasoning));
        Assert.Contains(result.Response.Messages.Single().Contents, c => c is TextReasoningContent);
    }

    [Fact]
    public async Task Workflow_ReplayOff_StripsReasoningKeptUnderAnEarlierSetting()
    {
        // The setting changed between the two calls (e.g. a restart with new configuration).
        var calls = new List<List<WorkflowChatMessage>>();
        var result = await RunToolLoop(ReasoningReplay.AcrossTurns, calls, finalReplay: ReasoningReplay.Off);

        Assert.All(result.TurnMessages, m => Assert.Null(m.Reasoning));
    }

    [Theory]
    [InlineData(ReasoningReplay.WithinRun)]
    [InlineData(ReasoningReplay.Off)]
    public async Task Workflow_ReasoningFromEarlierTurns_IsNotCarriedPastTheFirstCall(ReasoningReplay replay)
    {
        var calls = new List<List<WorkflowChatMessage>>();
        var prior = new List<WorkflowChatMessage>
        {
            new() { Role = "user", Content = "q1" },
            new() { Role = "assistant", Content = "a1", Reasoning = [new WorkflowReasoningContent { Text = "old", Source = AgentName }] }
        };

        await RunToolLoop(replay, calls, priorMessages: prior);

        Assert.NotNull(calls[0][1].Reasoning);
        Assert.Null(calls[1][1].Reasoning);
        // The caller's own history is not modified.
        Assert.NotNull(prior[1].Reasoning);
    }

    [Fact]
    public async Task Workflow_PassesTheIterationOfEachCall()
    {
        var iterations = new List<int?>();
        var context = MakeWorkflowContext((name, input) =>
        {
            if (name == nameof(CallLlmActivity))
            {
                iterations.Add(((CallLlmInput)input!).Iteration);
                return Task.FromResult<object?>(iterations.Count == 1
                    ? new CallLlmOutput { FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "lookup" }] }
                    : new CallLlmOutput { IsFinal = true, Text = "done" });
            }

            var toolInput = (ExecuteToolInput)input!;
            return Task.FromResult<object?>(new ExecuteToolOutput { CallId = toolInput.CallId, FunctionName = toolInput.FunctionName });
        });

        await new AgentRunWorkflow().RunAsync(context, new DaprAgentInvocation(AgentName, "q", null, null));

        Assert.Equal([0, 1], iterations);
    }

    [Fact]
    public async Task Workflow_OutputsWithoutReplaySetting_BehaveAsAcrossTurns()
    {
        // Outputs recorded before the setting existed replay with ReasoningReplay null.
        var calls = new List<List<WorkflowChatMessage>>();
        var result = await RunToolLoop(replay: null, calls);

        Assert.Contains(result.TurnMessages, m => m.Reasoning is { Count: > 0 });
    }

    [Fact]
    public async Task Workflow_PassesWhereTheRunsMessagesStart()
    {
        CallLlmInput? captured = null;
        var context = MakeWorkflowContext((_, input) =>
        {
            captured = (CallLlmInput)input!;
            return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "ok" });
        });
        var prior = new List<WorkflowChatMessage>
        {
            new() { Role = "user", Content = "q1" },
            new() { Role = "assistant", Content = "a1" }
        };

        await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "q2", null, null) { PriorMessages = prior });

        Assert.Equal(2, captured!.RunMessagesStart);
    }

    // ── Warning for a client that cannot send the effort ───────────────────

    [Fact]
    public async Task CallLlm_EffortWithDaprChatClient_WarnsOncePerAgent()
    {
        var logger = new ListLogger();
        var agentName = "dapr-agent";
        var registry = new ChatClientRegistry();
        registry.Register(agentName, new ToolResultCompatibilityChatClient(new DaprChatClient()), null, null);
        var sp = new EmptyServiceProvider();
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp, logger,
            reasoningEfforts: [new ReasoningEffortRegistration(null, ReasoningEffort.High, null)]);
        var input = new CallLlmInput(agentName, null, [new WorkflowChatMessage { Role = "user", Content = "q" }]);

        await activity.RunAsync(MakeContext(), input);
        await activity.RunAsync(MakeContext(), input);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(agentName));
    }

    [Fact]
    public async Task CallLlm_EffortWithDaprChatClientBehindAnotherWrapper_Warns_AndEachHostWarnsAgain()
    {
        // Behind a DelegatingChatClient (logging, caching, ...) it is still the Dapr client that ignores it.
        var wrapped = new PassThroughChatClient(new ToolResultCompatibilityChatClient(new DaprChatClient()));
        var efforts = new[] { new ReasoningEffortRegistration(null, ReasoningEffort.High, null) };
        var input = new CallLlmInput(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "q" }]);

        for (var host = 0; host < 2; host++)
        {
            var logger = new ListLogger();
            var registry = new ChatClientRegistry();
            registry.Register(AgentName, wrapped, null, null);
            var sp = new EmptyServiceProvider();
            var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp, logger,
                reasoningEfforts: efforts);

            await activity.RunAsync(MakeContext(), input);

            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        }
    }

    [Fact]
    public async Task CallLlm_OutputOnlyWithDaprChatClient_DoesNotWarnAboutEffort()
    {
        var logger = new ListLogger();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, new DaprChatClient(), null, null, null,
            new ChatOptions { Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary } });
        var sp = new EmptyServiceProvider();
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp, logger);

        await activity.RunAsync(MakeContext(), UserInput());

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    // ── Builder ────────────────────────────────────────────────────────────

    [Fact]
    public void Builder_WithReasoningAndReplay_RegisterSettings()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents()
            .WithReasoning(ReasoningEffort.Medium)
            .WithReasoning(AgentName, ReasoningEffort.High, ReasoningOutput.Summary)
            .WithReasoningReplay(ReasoningReplay.WithinRun)
            .WithReasoningReplay(AgentName, ReasoningReplay.Off);

        var provider = services.BuildServiceProvider();
        var efforts = provider.GetServices<ReasoningEffortRegistration>().ToList();
        var replays = provider.GetServices<ReasoningReplayRegistration>().ToList();
        Assert.Equal(ReasoningEffort.High, ReasoningEffortRegistration.Resolve(efforts, AgentName, null)!.Effort);
        Assert.Equal(ReasoningOutput.Summary, ReasoningEffortRegistration.Resolve(efforts, AgentName, null)!.Output);
        Assert.Equal(ReasoningEffort.Medium, ReasoningEffortRegistration.Resolve(efforts, "other", null)!.Effort);
        Assert.Equal(ReasoningReplay.Off, ReasoningReplayRegistration.Resolve(replays, AgentName));
        Assert.Equal(ReasoningReplay.WithinRun, ReasoningReplayRegistration.Resolve(replays, "other"));
    }

    [Fact]
    public void Builder_RejectsUndefinedValuesAndBlankAgentNames()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithReasoning((ReasoningEffort)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithReasoning(ReasoningEffort.Low, (ReasoningOutput)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithReasoningReplay((ReasoningReplay)42));
        Assert.Throws<ArgumentException>(() => builder.WithReasoning(" ", ReasoningEffort.Low));
        Assert.Throws<ArgumentException>(() => builder.WithReasoningReplay(" ", ReasoningReplay.Off));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (CallLlmActivity Activity, CallLlmActivityTests.CapturingChatClient Client) BuildActivity(
        ChatOptions? baseOptions = null,
        ReasoningEffortRegistration[]? efforts = null,
        ReasoningReplayRegistration[]? replays = null)
    {
        var sp = new EmptyServiceProvider();
        var client = new CallLlmActivityTests.CapturingChatClient();
        client.SetNextResponse(finalText: "ok");
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, client, null, null, null, baseOptions);
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp,
            NullLogger<CallLlmActivity>.Instance, reasoningEfforts: efforts, reasoningReplays: replays);
        return (activity, client);
    }

    private static TestWorkflowActivityContext MakeContext() => new("instance-1");

    private static CallLlmInput UserInput() =>
        new(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = "question" }]);

    // An earlier turn (messages 0-1) and this run (from message 2), each with reasoning by this agent.
    private static CallLlmInput HistoryInput(int runMessagesStart) =>
        new(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "q1" },
            new WorkflowChatMessage
            {
                Role = "assistant", Content = "a1",
                Reasoning = [new WorkflowReasoningContent { Text = "earlier turn", Source = AgentName }]
            },
            new WorkflowChatMessage { Role = "user", Content = "q2" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "lookup" }],
                Reasoning = [new WorkflowReasoningContent { Text = "this run", Source = AgentName }]
            },
            new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults = [new WorkflowFunctionResult { CallId = "c1", Name = "lookup", ResultJson = "1" }]
            }
        ])
        { RunMessagesStart = runMessagesStart };

    // One tool call, then a final answer; both LLM outputs carry reasoning and the given replay setting.
    private static Task<AgentRunResult> RunToolLoop(
        ReasoningReplay? replay,
        List<List<WorkflowChatMessage>> calls,
        ReasoningReplay? finalReplay = null,
        List<WorkflowChatMessage>? priorMessages = null)
    {
        finalReplay ??= replay;
        var context = MakeWorkflowContext((name, input) =>
        {
            if (name == nameof(CallLlmActivity))
            {
                calls.Add(((CallLlmInput)input!).Messages.ToList());
                return Task.FromResult<object?>(calls.Count == 1
                    ? new CallLlmOutput
                    {
                        FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "lookup" }],
                        Reasoning = [new WorkflowReasoningContent { Text = "need a lookup", Source = AgentName }],
                        ReasoningReplay = replay
                    }
                    : new CallLlmOutput
                    {
                        IsFinal = true,
                        Text = "done",
                        Reasoning = [new WorkflowReasoningContent { Text = "final", Source = AgentName }],
                        ReasoningReplay = finalReplay
                    });
            }

            var toolInput = (ExecuteToolInput)input!;
            return Task.FromResult<object?>(new ExecuteToolOutput { CallId = toolInput.CallId, FunctionName = toolInput.FunctionName, ResultJson = "1" });
        });

        return new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "question", null, null) { PriorMessages = priorMessages });
    }

    private static TestWorkflowContext MakeWorkflowContext(Func<string, object?, Task<object?>> handler) =>
        new("workflow-reasoning-options", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
            _ => handler(name, input)
        });

    // Named like the Dapr client: the warning recognises it by type name.
    private sealed class DaprChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class PassThroughChatClient(IChatClient inner) : DelegatingChatClient(inner);

    private sealed class ListLogger : ILogger<CallLlmActivity>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
