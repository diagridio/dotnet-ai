// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class CompactionPlannerTests
{
    private static ConversationEntry Entry(long sequence, string turnId, WorkflowChatMessage message) =>
        new(sequence, turnId, "agent", null, DateTimeOffset.UnixEpoch, message);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void BuildTranscript_WritesTheEarlierSummaryThenEachMessage()
    {
        var previous = Entry(0, "s", new WorkflowChatMessage { Role = "system", Content = "S1" });
        var older = new List<ConversationEntry>
        {
            Entry(1, "t", new WorkflowChatMessage { Role = "user", Content = "hi" }),
            Entry(2, "t", new WorkflowChatMessage
            {
                Role = "assistant",
                FunctionCalls = [new WorkflowFunctionCall { CallId = "c", Name = "lookup", ArgumentsJson = "{\"q\":1}" }]
            }),
            Entry(3, "t", new WorkflowChatMessage
            {
                Role = "tool",
                FunctionResults = [new WorkflowFunctionResult { CallId = "c", Name = "lookup", ResultJson = null }]
            }),
        };

        var transcript = CompactionPlanner.BuildTranscript(previous, older,
            new ConversationCompactionOptions { TriggerTokens = 10_000 });

        Assert.Equal(
            "Earlier summary:\nS1\n\nConversation to summarize:\nuser: hi\nassistant called lookup {\"q\":1}\ntool result: (none)\n",
            Normalize(transcript));
    }

    [Fact]
    public void BuildTranscript_CutsToTheSummaryInputBudget()
    {
        var older = new List<ConversationEntry>
        {
            Entry(0, "t", new WorkflowChatMessage { Role = "user", Content = new string('x', 1000) })
        };

        var transcript = CompactionPlanner.BuildTranscript(null, older,
            new ConversationCompactionOptions { TriggerTokens = 10_000, MaxSummaryInputTokens = 10 });

        Assert.StartsWith("Conversation to summarize:", transcript, StringComparison.Ordinal);
        Assert.EndsWith("[truncated]", transcript, StringComparison.Ordinal);
        Assert.Equal(10 * TokenEstimate.CharsPerToken + Environment.NewLine.Length + "[truncated]".Length, transcript.Length);
    }

    [Fact]
    public void PlanCompaction_SummarizesTheOldestTurnsWithinTheInputBudget()
    {
        static List<ConversationEntry> Turn(string id, long seq, int tokens) =>
            [Entry(seq, id, new WorkflowChatMessage { Role = "user", Content = new string('x', tokens * TokenEstimate.CharsPerToken) })];
        var turns = new List<List<ConversationEntry>> { Turn("a", 0, 50), Turn("b", 1, 50), Turn("c", 2, 50), Turn("d", 3, 10) };

        var plan = CompactionPlanner.PlanCompaction(turns, null,
            new ConversationCompactionOptions { TriggerTokens = 1000, KeepRecentTurns = 1, MaxSummaryInputTokens = 100 });

        // Two 50-token turns fill the 100-token budget; the third waits for the next compaction.
        Assert.Equal(2, plan!.Value.CoversUpTo);
        Assert.Equal([0L, 1L], plan.Value.Older.Select(e => e.Sequence));
    }
}
