// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies that <see cref="CallLlmActivity"/> correctly converts
/// <see cref="WorkflowChatMessage"/> instances — including tool-call and tool-result
/// payloads — to <see cref="ChatMessage"/> objects before forwarding to the
/// <see cref="IChatClient"/>.
/// </summary>
public sealed class CallLlmActivityTests
{
    private const string AgentName = "test-agent";

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (CallLlmActivity Activity, CapturingChatClient Client) BuildActivity()
    {
        var sp = new EmptyServiceProvider();
        var capturingClient = new CapturingChatClient();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, capturingClient, instructions: null, tools: null);

        var agentRegistry = new AgentRegistry(sp, []);
        var activity = new CallLlmActivity(registry, agentRegistry, sp, NullLogger<CallLlmActivity>.Instance);

        return (activity, capturingClient);
    }

    private static TestWorkflowActivityContext MakeContext() =>
        new TestWorkflowActivityContext("instance-1");

    // ── User / assistant text messages ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_UserMessage_ForwardedWithUserRole()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "ok");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "hello" }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var msgs = client.LastMessages!;
        Assert.Contains(msgs, m => m.Role == ChatRole.User && m.Text == "hello");
    }

    [Fact]
    public async Task RunAsync_Instructions_PrependedAsSystemMessage()
    {
        var sp = new EmptyServiceProvider();
        var capturingClient = new CapturingChatClient();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, capturingClient, instructions: "You are helpful.", tools: null);

        var activity = new CallLlmActivity(
            registry, new AgentRegistry(sp, []), sp,
            NullLogger<CallLlmActivity>.Instance);

        capturingClient.SetNextResponse(finalText: "ok");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "hi" }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var msgs = capturingClient.LastMessages!;
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("You are helpful.", msgs[0].Text);
    }

    // ── FunctionCallContent round-trip ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_AssistantWithFunctionCalls_ForwardedAsFunctionCallContent()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "done");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "go" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls =
                [
                    new WorkflowFunctionCall
                    {
                        CallId = "call-42",
                        Name = "my_tool",
                        ArgumentsJson = "{\"x\":1}"
                    }
                ]
            }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var assistantMsg = client.LastMessages!.Single(m => m.Role == ChatRole.Assistant);
        var fcc = Assert.Single(assistantMsg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("call-42", fcc.CallId);
        Assert.Equal("my_tool", fcc.Name);
    }

    // ── FunctionResultContent round-trip ───────────────────────────────────

    [Fact]
    public async Task RunAsync_ToolMessage_ForwardedAsFunctionResultContent()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "done");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "go" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "t", ArgumentsJson = "{}" }]
            },
            new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults =
                [
                    new WorkflowFunctionResult { CallId = "c1", Name = "t", ResultJson = "\"output\"" }
                ]
            }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var toolMsg = client.LastMessages!.Single(m => m.Role == ChatRole.Tool);
        var frc = Assert.Single(toolMsg.Contents.OfType<FunctionResultContent>());
        Assert.Equal("c1", frc.CallId);
    }

    [Fact]
    public async Task RunAsync_ToolMessage_CallIdMatchesAssistantFunctionCall()
    {
        // Regression guard: ensures the same CallId flows from FunctionCall → FunctionResult.
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "done");

        const string callId = "unique-call-id-99";

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "run" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = callId, Name = "fn", ArgumentsJson = "{}" }]
            },
            new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults = [new WorkflowFunctionResult { CallId = callId, Name = "fn", ResultJson = "null" }]
            }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var msgs = client.LastMessages!;

        var fcCallId = msgs
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(fc => fc.CallId)
            .Single();

        var frCallId = msgs
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(fr => fr.CallId)
            .Single();

        Assert.Equal(fcCallId, frCallId);
    }

    [Fact]
    public async Task RunAsync_ToolMessage_MultipleResults_AllForwardedWithCorrectCallIds()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "done");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "go" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls =
                [
                    new WorkflowFunctionCall { CallId = "id-A", Name = "toolA", ArgumentsJson = "{}" },
                    new WorkflowFunctionCall { CallId = "id-B", Name = "toolB", ArgumentsJson = "{}" }
                ]
            },
            new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults =
                [
                    new WorkflowFunctionResult { CallId = "id-A", Name = "toolA", ResultJson = "1" },
                    new WorkflowFunctionResult { CallId = "id-B", Name = "toolB", ResultJson = "2" }
                ]
            }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var toolMsg = client.LastMessages!.Single(m => m.Role == ChatRole.Tool);
        var results = toolMsg.Contents.OfType<FunctionResultContent>().ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.CallId == "id-A");
        Assert.Contains(results, r => r.CallId == "id-B");
    }

    // ── IsFinal detection ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_LlmReturnsFinalText_IsFinalIsTrue()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "final answer");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "q" }
        ]);

        var output = await activity.RunAsync(MakeContext(), input);

        Assert.True(output.IsFinal);
        Assert.Equal("final answer", output.Text);
    }

    [Fact]
    public async Task RunAsync_LlmReturnsFunctionCall_IsFinalIsFalse()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(toolCallId: "c1", toolName: "fn");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "q" }
        ]);

        var output = await activity.RunAsync(MakeContext(), input);

        Assert.False(output.IsFinal);
        Assert.NotNull(output.FunctionCalls);
        Assert.Single(output.FunctionCalls);
        Assert.Equal("c1", output.FunctionCalls![0].CallId);
    }

    // ── Helper: CapturingChatClient ────────────────────────────────────────

    internal sealed class CapturingChatClient : IChatClient
    {
        public List<ChatMessage>? LastMessages { get; private set; }

        private ChatResponse? _nextResponse;

        public void SetNextResponse(string? finalText = null, string? toolCallId = null, string? toolName = null)
        {
            if (toolCallId is not null && toolName is not null)
            {
                _nextResponse = new ChatResponse(
                    new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent(toolCallId, toolName)]));
            }
            else
            {
                _nextResponse = new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, finalText ?? string.Empty));
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            return Task.FromResult(_nextResponse
                ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
