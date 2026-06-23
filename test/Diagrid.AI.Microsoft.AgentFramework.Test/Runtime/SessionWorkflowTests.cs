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
        var turnRequest = new SessionTurnRequest
        {
            AgentName = "alpha",
            ChatClientKey = "key",
            Message = "hello",
            TurnId = "turn-1",
            Options = options
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
    }
}
