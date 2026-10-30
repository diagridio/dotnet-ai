using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class SessionWorkflowTests
{
    [Fact]
    public async Task RunAsync_ForwardsTurnRequestOptionsToAgentRunWorkflow()
    {
        DaprAgentInvocation? captured = null;
        string? childWorkflowName = null;
        var options = new AgentRunOptions();
        var telemetryBaggage = new Dictionary<string, string?> { ["tenant.id"] = "tenant-1" };
        var turnRequest = new SessionTurnRequest
        {
            AgentName = "alpha",
            ChatClientKey = "key",
            Message = "hello",
            TurnId = "turn-1",
            Options = options,
            TelemetryBaggage = telemetryBaggage
        };

        var context = new TestWorkflowContext(
            "session-1",
            (name, input) =>
            {
                childWorkflowName = name;
                captured = (DaprAgentInvocation)input!;
                return Task.FromResult<object?>(new AgentRunResult
                {
                    Response = new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok"))
                });
            },
            [(SessionWorkflow.TurnEventName, turnRequest)]);

        var result = await new SessionWorkflow().RunAsync(
            context,
            new SessionWorkflowInput { MaxTurns = 1 });

        Assert.Equal("Session completed after 1 turns.", result);
        Assert.Equal(nameof(AgentRunWorkflow), childWorkflowName);
        Assert.NotNull(captured);
        Assert.Equal("alpha", captured!.AgentName);
        Assert.Equal("key", captured.ChatClientKey);
        Assert.Equal("hello", captured.Message);
        Assert.Same(options, captured.Options);
        Assert.Same(telemetryBaggage, captured.TelemetryBaggage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("conv-1")]
    public async Task RunAsync_CarriesTheLogOrRunsAgainstTheStoredConversation(string? conversationId)
    {
        var captured = new List<DaprAgentInvocation>();
        var context = new TestWorkflowContext(
            "session-1",
            (_, input) =>
            {
                var invocation = (DaprAgentInvocation)input!;
                captured.Add(invocation with { PriorMessages = invocation.PriorMessages?.ToList() });
                return Task.FromResult<object?>(new AgentRunResult
                {
                    Response = new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok")),
                    TurnMessages = [new WorkflowChatMessage { Role = "user", Content = invocation.Message }, new WorkflowChatMessage { Role = "assistant", Content = "ok" }]
                });
            },
            [
                (SessionWorkflow.TurnEventName, new SessionTurnRequest { AgentName = "alpha", Message = "one", TurnId = "t1" }),
                (SessionWorkflow.TurnEventName, new SessionTurnRequest { AgentName = "alpha", Message = "two", TurnId = "t2" }),
            ]);

        await new SessionWorkflow().RunAsync(context, new SessionWorkflowInput { MaxTurns = 2, ConversationId = conversationId });

        Assert.Equal(2, captured.Count);
        Assert.All(captured, c => Assert.Equal(conversationId, c.ConversationId));
        Assert.Null(captured[0].PriorMessages);
        if (conversationId is null)
        {
            Assert.Equal(["one", "ok"], captured[1].PriorMessages!.Select(m => m.Content));
        }
        else
        {
            Assert.Null(captured[1].PriorMessages);
        }
    }
}
