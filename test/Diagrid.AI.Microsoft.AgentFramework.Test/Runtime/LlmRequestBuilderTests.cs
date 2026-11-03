// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class LlmRequestBuilderTests
{
    private const string Source = "agent";

    private static WorkflowChatMessage Assistant(string text) => new()
    {
        Role = "assistant",
        Content = text,
        Reasoning = [new WorkflowReasoningContent { Text = "thought " + text, Source = Source }]
    };

    [Fact]
    public void BuildChatMessages_PutsInstructionsAndContextFirst_AndCountsEachPart()
    {
        var layout = LlmRequestBuilder.BuildChatMessages(
            "base", "extra",
            [new WorkflowChatMessage { Role = "user", Content = "memory" }],
            [new WorkflowChatMessage { Role = "user", Content = "old" }, Assistant("a1"), new WorkflowChatMessage { Role = "user", Content = "new" }],
            Source, ReasoningReplay.AcrossTurns, runMessagesStart: 2, historyStartsWithSummary: true);

        Assert.Equal(5, layout.Messages.Count);
        Assert.Equal(ChatRole.System, layout.Messages[0].Role);
        Assert.Equal("base\n\nextra", layout.Messages[0].Text);
        Assert.Equal("memory", layout.Messages[1].Text);
        Assert.Equal(2, layout.HeadCount);
        Assert.Equal(2, layout.HistoryCount);
        Assert.True(layout.HistoryStartsWithSummary);
    }

    [Fact]
    public void BuildChatMessages_HistoryCount_NeverExceedsTheMessages()
    {
        var layout = LlmRequestBuilder.BuildChatMessages(null, null, null,
            [new WorkflowChatMessage { Role = "user", Content = "hi" }],
            Source, ReasoningReplay.AcrossTurns, runMessagesStart: 5, historyStartsWithSummary: false);

        Assert.Equal(0, layout.HeadCount);
        Assert.Equal(1, layout.HistoryCount);
    }

    [Theory]
    [InlineData(ReasoningReplay.AcrossTurns, true, true)]
    [InlineData(ReasoningReplay.WithinRun, false, true)]
    [InlineData(ReasoningReplay.Off, false, false)]
    public void BuildChatMessages_SendsReasoningAsTheReplaySettingSays(ReasoningReplay replay, bool earlierTurn, bool thisRun)
    {
        var layout = LlmRequestBuilder.BuildChatMessages(null, null, null,
            [Assistant("earlier"), Assistant("now")],
            Source, replay, runMessagesStart: 1, historyStartsWithSummary: false);

        Assert.Equal(earlierTurn, layout.Messages[0].Contents.OfType<TextReasoningContent>().Any());
        Assert.Equal(thisRun, layout.Messages[1].Contents.OfType<TextReasoningContent>().Any());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("a", null, "a")]
    [InlineData(" ", "b", "b")]
    [InlineData("a", "b", "a\n\nb")]
    public void CombineInstructions_JoinsWhatIsSet(string? instructions, string? additional, string? expected)
    {
        Assert.Equal(expected, LlmRequestBuilder.CombineInstructions(instructions, additional));
    }

    [Fact]
    public void BuildChatOptions_NothingToSend_ReturnsNull()
    {
        Assert.Null(LlmRequestBuilder.BuildChatOptions(null, null, null, null, null, isFollowUpCall: false));
    }

    [Fact]
    public void BuildChatOptions_ClonesTheAgentsOptions_AndLetsTheRunOverrideOnlyWhatItSets()
    {
        var agentFormat = ChatResponseFormat.Json;
        var baseOptions = new ChatOptions
        {
            Temperature = 0.2f,
            ToolMode = ChatToolMode.RequireAny,
            ResponseFormat = agentFormat,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["a"] = 1, ["b"] = 1 }
        };
        var run = new AgentRunOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["b"] = 2 } };
        var reasoning = new ReasoningOptions { Effort = ReasoningEffort.High };

        var options = LlmRequestBuilder.BuildChatOptions(baseOptions, null, null, run, reasoning, isFollowUpCall: true)!;

        Assert.NotSame(baseOptions, options);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Same(reasoning, options.Reasoning);
        Assert.Null(options.ToolMode);
        Assert.Same(agentFormat, options.ResponseFormat);
        Assert.Equal(1, options.AdditionalProperties!["a"]);
        Assert.Equal(2, options.AdditionalProperties["b"]);
        Assert.Equal(ChatToolMode.RequireAny, baseOptions.ToolMode);
        Assert.Equal(1, baseOptions.AdditionalProperties["b"]);
    }

    [Fact]
    public void CombineTools_AgentToolsThenContributed_OrNullWhenNone()
    {
        AITool a = AIFunctionFactory.Create(() => "a", "a");
        AITool b = AIFunctionFactory.Create(() => "b", "b");

        Assert.Null(LlmRequestBuilder.CombineTools(null, []));
        Assert.Equal([a, b], LlmRequestBuilder.CombineTools([a], [b])!);
        Assert.Equal([b], LlmRequestBuilder.CombineTools(null, [b])!);
    }

    [Fact]
    public void ParseResponse_ToolCalls_AreNotFinal_AndCarryTheirArguments()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("c1", "lookup", new Dictionary<string, object?> { ["q"] = "x" }),
            new FunctionCallContent("c2", "now")
        ]));

        var output = LlmRequestBuilder.ParseResponse(response, Source);

        Assert.False(output.IsFinal);
        var calls = output.FunctionCalls!;
        Assert.Equal(["c1", "c2"], calls.Select(f => f.CallId));
        Assert.Equal("{\"q\":\"x\"}", calls[0].ArgumentsJson);
        Assert.Equal("{}", calls[1].ArgumentsJson);
    }

    [Fact]
    public void ParseResponse_ReadsReasoningFromEveryMessage_AndTextFromTheLast()
    {
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("thinking")]),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]);

        var output = LlmRequestBuilder.ParseResponse(response, Source);

        Assert.True(output.IsFinal);
        Assert.Equal("answer", output.Text);
        Assert.Null(output.FunctionCalls);
        Assert.Equal("thinking", Assert.Single(output.Reasoning!).Text);
        Assert.Equal(Source, output.Reasoning![0].Source);
    }

    [Fact]
    public void ParseResponse_NoMessages_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LlmRequestBuilder.ParseResponse(new ChatResponse(), Source));
    }
}
