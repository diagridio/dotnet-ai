// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// Verifies that model reasoning (<see cref="TextReasoningContent"/>) is captured from the LLM
/// response, kept on the assistant messages of the run, sent back to the model on later calls,
/// returned to the caller, and kept away from AIContextProviders.
/// </summary>
public sealed class ReasoningTests
{
    private const string AgentName = "reasoning-agent";

    // Dapr Workflow serializes activity and workflow payloads with JsonSerializerDefaults.Web.
    private static readonly JsonSerializerOptions WorkflowSerializerOptions = new(JsonSerializerDefaults.Web);

    // ── Capture (CallLlmActivity) ──────────────────────────────────────────

    [Fact]
    public async Task CallLlm_ResponseWithReasoning_ReturnsReasoningInOrder()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("first thought"),
            new TextReasoningContent("second thought") { ProtectedData = "sig-2" },
            new TextContent("answer")
        ])));

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        Assert.True(output.IsFinal);
        Assert.Equal("answer", output.Text);
        Assert.NotNull(output.Reasoning);
        Assert.Collection(output.Reasoning!,
            r => { Assert.Equal("first thought", r.Text); Assert.Null(r.ProtectedData); Assert.Equal(AgentName, r.Source); },
            r => { Assert.Equal("second thought", r.Text); Assert.Equal("sig-2", r.ProtectedData); Assert.Equal(AgentName, r.Source); });
    }

    [Fact]
    public async Task CallLlm_ReasoningInSeparateMessage_IsCaptured()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("thinking")]),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]));

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        Assert.Equal("answer", output.Text);
        Assert.Equal("thinking", Assert.Single(output.Reasoning!).Text);
    }

    [Fact]
    public async Task CallLlm_ProtectedDataOnlyReasoning_IsKept_AndEmptyBlocksAreDropped()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent(string.Empty),
            new TextReasoningContent(null) { ProtectedData = "encrypted" },
            new TextContent("answer")
        ])));

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        var block = Assert.Single(output.Reasoning!);
        Assert.Null(block.Text);
        Assert.Equal("encrypted", block.ProtectedData);
        // Without text it would come back as an ordinary thinking block (Anthropic redacted_thinking),
        // so it is kept for the caller but never sent back.
        Assert.Null(block.Source);
    }

    [Fact]
    public async Task CallLlm_OpenAIResponsesReasoningItem_IsKeptButNotReplayable()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            // The rs_ id of an OpenAI Responses reasoning item lives only in this object, which does
            // not survive the workflow, and a rebuilt item without it is rejected.
            new TextReasoningContent("summary") { RawRepresentation = CreateOpenAIReasoningItem() },
            new TextContent("answer")
        ])));

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        var block = Assert.Single(output.Reasoning!);
        Assert.Equal("summary", block.Text);
        Assert.Null(block.Source);
    }

    [Fact]
    public async Task CallLlm_OtherRawRepresentationWithAnId_IsStillReplayable()
    {
        // Any other provider object, even one with an Id (e.g. a whole chat completion), does not stop
        // the reasoning from being sent back.
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("thought") { RawRepresentation = new ProviderItem("chatcmpl-123") },
            new TextContent("answer")
        ])));

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        Assert.Equal(AgentName, Assert.Single(output.Reasoning!).Source);
    }

    [Fact]
    public async Task CallLlm_WithChatClientKey_RecordsAgentAndKeyAsSource()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("thought"), new TextContent("answer")])));

        var output = await activity.RunAsync(MakeContext(),
            new CallLlmInput(AgentName, "key-a", [new WorkflowChatMessage { Role = "user", Content = "q" }]));

        Assert.Equal($"{AgentName}/key-a", Assert.Single(output.Reasoning!).Source);
    }

    [Fact]
    public async Task CallLlm_ResponseWithoutReasoning_LeavesReasoningNull()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "answer");

        var output = await activity.RunAsync(MakeContext(), UserInput("question"));

        Assert.Null(output.Reasoning);
    }

    [Fact]
    public async Task CallLlm_ResponseWithNoMessages_ThrowsClearError()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(new ChatResponse(new List<ChatMessage>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => activity.RunAsync(MakeContext(), UserInput("question")));

        Assert.Contains("no messages", ex.Message);
    }

    // ── Resend (CallLlmActivity → IChatClient) ─────────────────────────────

    [Fact]
    public async Task CallLlm_HistoryWithReasoning_SendsReasoningBeforeTextAndToolCalls()
    {
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "ok");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "question" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                Content = "calling a tool",
                FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "lookup" }],
                Reasoning = [new WorkflowReasoningContent { Text = "I need data", ProtectedData = "sig", Source = AgentName }]
            },
            new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults = [new WorkflowFunctionResult { CallId = "c1", Name = "lookup", ResultJson = "42" }]
            }
        ]);

        await activity.RunAsync(MakeContext(), input);

        var assistant = client.LastMessages![1];
        Assert.Equal(ChatRole.Assistant, assistant.Role);
        var reasoning = Assert.IsType<TextReasoningContent>(assistant.Contents[0]);
        Assert.Equal("I need data", reasoning.Text);
        Assert.Equal("sig", reasoning.ProtectedData);
        Assert.IsType<TextContent>(assistant.Contents[1]);
        Assert.IsType<FunctionCallContent>(assistant.Contents[2]);
        Assert.Equal("calling a tool", assistant.Text);
    }

    [Theory]
    [InlineData("other-agent")]
    [InlineData(AgentName + "/other-key")]
    [InlineData(null)]
    public async Task CallLlm_ReasoningFromAnotherSource_IsNotSent(string? source)
    {
        // History shared from another agent, another chat client, or a context provider: another
        // provider's signed or encrypted reasoning would be rejected, so none of it goes out.
        var (activity, client) = BuildActivity();
        client.SetNextResponse(finalText: "ok");

        var input = new CallLlmInput(AgentName, null,
        [
            new WorkflowChatMessage { Role = "user", Content = "question" },
            new WorkflowChatMessage
            {
                Role = "assistant",
                Content = "earlier answer",
                Reasoning = [new WorkflowReasoningContent { Text = "foreign", ProtectedData = "sig", Source = source }]
            },
            new WorkflowChatMessage { Role = "user", Content = "follow-up" }
        ]);

        await activity.RunAsync(MakeContext(), input);

        Assert.DoesNotContain(client.LastMessages!.SelectMany(m => m.Contents), c => c is TextReasoningContent);
        Assert.Equal("earlier answer", client.LastMessages![1].Text);
    }

    // ── Converter ──────────────────────────────────────────────────────────

    [Fact]
    public void Converter_RoundTripsReasoning()
    {
        var original = new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("because") { ProtectedData = "sig" },
            new TextContent("answer")
        ]);

        var workflowMessage = WorkflowChatMessageConverter.FromChatMessage(original);
        var block = Assert.Single(workflowMessage.Reasoning!);
        var replayable = workflowMessage with { Reasoning = [block with { Source = AgentName }] };
        var roundTripped = WorkflowChatMessageConverter.ToChatMessage(replayable, AgentName);

        Assert.Equal("answer", workflowMessage.Content);
        Assert.Equal("because", block.Text);
        Assert.Equal("sig", block.ProtectedData);
        // A message contributed by a context provider has no known source, so it is never sent back.
        Assert.Null(block.Source);
        var reasoning = Assert.IsType<TextReasoningContent>(roundTripped.Contents[0]);
        Assert.Equal("because", reasoning.Text);
        Assert.Equal("sig", reasoning.ProtectedData);
        Assert.Equal("answer", roundTripped.Text);
    }

    [Fact]
    public void Converter_ToProviderMessage_LeavesReasoningOut()
    {
        var message = new WorkflowChatMessage
        {
            Role = "assistant",
            Content = "answer",
            Reasoning = [new WorkflowReasoningContent { Text = "private thoughts", Source = AgentName }]
        };

        var providerMessage = WorkflowChatMessageConverter.ToProviderMessage(message);

        Assert.DoesNotContain(providerMessage.Contents, c => c is TextReasoningContent);
        Assert.Equal("answer", providerMessage.Text);
    }

    [Fact]
    public void Converter_CreateAssistantMessage_PutsReasoningFirst_AndKeepsTextUnchanged()
    {
        var message = WorkflowChatMessageConverter.CreateAssistantMessage(
            "answer", [new WorkflowReasoningContent { Text = "because" }]);

        Assert.IsType<TextReasoningContent>(message.Contents[0]);
        Assert.Equal("answer", message.Text);
    }

    // ── AgentRunWorkflow ───────────────────────────────────────────────────

    [Fact]
    public async Task Workflow_KeepsReasoningOnAssistantMessages_AndResendsItInTheToolLoop()
    {
        var llmCalls = new List<List<WorkflowChatMessage>>();
        var context = MakeWorkflowContext((name, input) =>
        {
            if (name == nameof(CallLlmActivity))
            {
                var llmInput = (CallLlmInput)input!;
                llmCalls.Add(llmInput.Messages.ToList());
                return Task.FromResult<object?>(llmCalls.Count == 1
                    ? new CallLlmOutput
                    {
                        IsFinal = false,
                        FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "lookup" }],
                        Reasoning = [new WorkflowReasoningContent { Text = "need a lookup", ProtectedData = "sig-1" }]
                    }
                    : new CallLlmOutput
                    {
                        IsFinal = true,
                        Text = "final answer",
                        Reasoning = [new WorkflowReasoningContent { Text = "lookup said 42" }]
                    });
            }

            var toolInput = (ExecuteToolInput)input!;
            return Task.FromResult<object?>(new ExecuteToolOutput { CallId = toolInput.CallId, FunctionName = toolInput.FunctionName, ResultJson = "42" });
        });

        var result = await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "question", null, null));

        // The second LLM call sees the first call's reasoning on the assistant tool-call message.
        var toolCallMessage = llmCalls[1].Single(m => m.FunctionCalls is { Count: > 0 });
        Assert.Equal("sig-1", Assert.Single(toolCallMessage.Reasoning!).ProtectedData);

        // TurnMessages carry the reasoning, so RunAgentWithHistoryAsync sends it back next turn.
        Assert.Collection(result.TurnMessages.Where(m => m.Role == "assistant"),
            m => Assert.Equal("need a lookup", Assert.Single(m.Reasoning!).Text),
            m => Assert.Equal("lookup said 42", Assert.Single(m.Reasoning!).Text));

        // The caller's response carries the final reasoning, and its Text is unchanged.
        Assert.Equal("final answer", result.Response.Text);
        var reasoning = result.Response.Messages.Single().Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("lookup said 42", reasoning.Text);
    }

    [Fact]
    public async Task Workflow_PriorMessagesWithReasoning_AreSentToTheModel()
    {
        List<WorkflowChatMessage>? sent = null;
        var context = MakeWorkflowContext((_, input) =>
        {
            // Copy now: the workflow keeps appending to the same list after the call.
            sent = ((CallLlmInput)input!).Messages.ToList();
            return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "second answer" });
        });

        var prior = new List<WorkflowChatMessage>
        {
            new() { Role = "user", Content = "first question" },
            new()
            {
                Role = "assistant",
                Content = "first answer",
                Reasoning = [new WorkflowReasoningContent { Text = "earlier reasoning" }]
            }
        };

        await new AgentRunWorkflow().RunAsync(context,
            new DaprAgentInvocation(AgentName, "second question", null, null) { PriorMessages = prior });

        var assistant = sent!.Single(m => m.Role == "assistant");
        Assert.Equal("earlier reasoning", Assert.Single(assistant.Reasoning!).Text);
    }

    // ── Serialization across workflow boundaries ───────────────────────────

    [Fact]
    public void WorkflowChatMessage_WithoutReasoningKey_DeserializesAsBefore()
    {
        // The shape written by versions before reasoning was kept, as stored in existing workflow histories.
        const string json = """{"role":"assistant","content":"hi","functionCalls":null,"functionResults":null}""";

        var message = JsonSerializer.Deserialize<WorkflowChatMessage>(json, WorkflowSerializerOptions)!;

        Assert.Equal("hi", message.Content);
        Assert.Null(message.Reasoning);
    }

    [Fact]
    public void WorkflowChatMessage_WithReasoning_RoundTrips()
    {
        var message = new WorkflowChatMessage
        {
            Role = "assistant",
            Content = "hi",
            Reasoning = [new WorkflowReasoningContent { Text = "because", ProtectedData = "sig" }]
        };

        var json = JsonSerializer.Serialize(message, WorkflowSerializerOptions);
        var back = JsonSerializer.Deserialize<WorkflowChatMessage>(json, WorkflowSerializerOptions)!;

        Assert.Equal(message.Reasoning, back.Reasoning);
    }

    [Fact]
    public void AgentRunResult_WithReasoningInResponse_RoundTripsThroughWorkflowSerializer()
    {
        var result = new AgentRunResult
        {
            Response = new AgentResponse(WorkflowChatMessageConverter.CreateAssistantMessage(
                "answer", [new WorkflowReasoningContent { Text = "because", ProtectedData = "sig" }])),
            TurnMessages = []
        };

        var json = JsonSerializer.Serialize(result, WorkflowSerializerOptions);
        var back = JsonSerializer.Deserialize<AgentRunResult>(json, WorkflowSerializerOptions)!;

        Assert.Equal("answer", back.Response.Text);
        var reasoning = back.Response.Messages.Single().Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("because", reasoning.Text);
        Assert.Equal("sig", reasoning.ProtectedData);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (CallLlmActivity Activity, CallLlmActivityTests.CapturingChatClient Client) BuildActivity()
    {
        var sp = new EmptyServiceProvider();
        var client = new CallLlmActivityTests.CapturingChatClient();
        var registry = new ChatClientRegistry();
        registry.Register(AgentName, client, instructions: null, tools: null);
        var activity = new CallLlmActivity(registry, new ToolRegistry(), new AgentRegistry(sp, []), sp,
            NullLogger<CallLlmActivity>.Instance);
        return (activity, client);
    }

    private static TestWorkflowActivityContext MakeContext() => new("instance-1");

    private static CallLlmInput UserInput(string text) =>
        new(AgentName, null, [new WorkflowChatMessage { Role = "user", Content = text }]);

    private static TestWorkflowContext MakeWorkflowContext(Func<string, object?, Task<object?>> handler) =>
        new("workflow-reasoning", (name, input) => name switch
        {
            nameof(ResolveAgentContextActivity) => Task.FromResult<object?>(new ResolveAgentContextOutput()),
            nameof(CompleteAgentContextActivity) => Task.FromResult<object?>(new CompleteAgentContextOutput()),
            _ => handler(name, input)
        });

    private sealed record ProviderItem(string Id);

    // The real OpenAI SDK type, so a rename there fails this test instead of silently replaying.
#pragma warning disable OPENAI001
    private static object CreateOpenAIReasoningItem() => new OpenAI.Responses.ReasoningResponseItem("summary");
#pragma warning restore OPENAI001

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
