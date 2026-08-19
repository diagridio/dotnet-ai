// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class CompleteAgentContextActivityTests
{
    private const string AgentName = "test-agent";

    [Fact]
    public async Task RunAsync_NoContextProviders_IsNoOp()
    {
        var (activity, _) = Build();

        var output = await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], [Msg("hello")], null));

        Assert.NotNull(output);
    }

    [Fact]
    public async Task RunAsync_Success_InvokesProviderWithRequestAndResponseMessages()
    {
        IEnumerable<ChatMessage>? capturedRequest = null;
        IEnumerable<ChatMessage>? capturedResponse = null;
        Exception? capturedException = null;

        var provider = new FakeContextProvider
        {
            OnInvoked = ctx =>
            {
                capturedRequest = ctx.RequestMessages;
                capturedResponse = ctx.ResponseMessages;
                capturedException = ctx.InvokeException;
            }
        };
        var (activity, _) = Build(provider);

        await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], [Msg("hello")], null));

        Assert.Null(capturedException);
        Assert.Contains(capturedRequest!, m => m.Text == "hi");
        Assert.Contains(capturedResponse!, m => m.Text == "hello");
    }

    [Fact]
    public async Task RunAsync_ErrorMessage_InvokesProviderWithException()
    {
        Exception? capturedException = null;
        var provider = new FakeContextProvider
        {
            OnInvoked = ctx => capturedException = ctx.InvokeException
        };
        var (activity, _) = Build(provider);

        await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], null, "the LLM call failed"));

        Assert.NotNull(capturedException);
        Assert.Equal("the LLM call failed", capturedException!.Message);
    }

    [Fact]
    public async Task RunAsync_ProviderThrows_IsSwallowedAndLogged()
    {
        // Unlike ResolveAgentContextActivity, a provider's InvokedAsync failure must never
        // fail an already-completed (or already-failed) run.
        var provider = new FakeContextProvider { ThrowOnInvoked = new InvalidOperationException("bookkeeping failed") };
        var (activity, _) = Build(provider);

        var output = await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], [Msg("hello")], null));

        Assert.NotNull(output);
    }

    [Fact]
    public async Task RunAsync_MultipleProviders_AllInvoked()
    {
        var invokedNames = new List<string>();
        var providerA = new FakeContextProvider { OnInvoked = _ => invokedNames.Add("A") };
        var providerB = new FakeContextProvider { OnInvoked = _ => invokedNames.Add("B") };
        var (activity, _) = Build(providerA, providerB);

        await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], [Msg("hello")], null));

        Assert.Equal(["A", "B"], invokedNames);
    }

    [Fact]
    public async Task RunAsync_OneProviderThrows_OtherProvidersStillInvoked()
    {
        var invoked = false;
        var throwing = new FakeContextProvider { ThrowOnInvoked = new InvalidOperationException("boom") };
        var healthy = new FakeContextProvider { OnInvoked = _ => invoked = true };
        var (activity, _) = Build(throwing, healthy);

        await activity.RunAsync(
            MakeContext(),
            new CompleteAgentContextInput(AgentName, null, [Msg("hi")], [Msg("hello")], null));

        Assert.True(invoked);
    }

    private static (CompleteAgentContextActivity Activity, ChatClientRegistry ChatClientRegistry) Build(
        params AIContextProvider[] providers)
    {
        var sp = new EmptyServiceProvider();
        var chatClientRegistry = new ChatClientRegistry();
        var agentRegistry = new AgentRegistry(sp, []);
        agentRegistry.AddFactory(_ => new TestAIAgent(AgentName), null, AgentName, sp);

        chatClientRegistry.Register(AgentName, new TestChatClient(), null, null, providers.Length > 0 ? providers : null);

        var activity = new CompleteAgentContextActivity(chatClientRegistry, agentRegistry, sp, NullLogger<CompleteAgentContextActivity>.Instance);
        return (activity, chatClientRegistry);
    }

    private static TestWorkflowActivityContext MakeContext(string instanceId = "instance-1") => new(instanceId);

    private static WorkflowChatMessage Msg(string text) => new() { Role = "user", Content = text };

    private sealed class FakeContextProvider : AIContextProvider
    {
        public Exception? ThrowOnInvoked { get; set; }
        public Action<InvokedContext>? OnInvoked { get; set; }

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AIContext());

        // Overrides the lower-level hook (not StoreAIContextAsync) because MAF's own InvokedAsync
        // template skips StoreAIContextAsync entirely when InvokedContext.InvokeException is set
        // (confirmed empirically) — InvokedCoreAsync is the only hook that observes both outcomes.
        protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
        {
            OnInvoked?.Invoke(context);

            if (ThrowOnInvoked is not null)
            {
                throw ThrowOnInvoked;
            }

            return base.InvokedCoreAsync(context, cancellationToken);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
