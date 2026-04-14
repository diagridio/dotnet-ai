using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentRunWorkflowTests
{
    [Fact]
    public async Task RunAsync_CallsInvokeAgentActivity()
    {
        DaprAgentInvocation? captured = null;
        string? activityName = null;

        var context = new TestWorkflowContext("workflow-1", (name, input) =>
        {
            activityName = name;
            captured = (DaprAgentInvocation)input!;
            return Task.FromResult<object?>(AgentRunResponseFactory.CreateWithText("{}"));
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "message", null, null) { ChatClientKey = "key" };

        var result = await workflow.RunAsync(context, invocation);

        Assert.NotNull(result);
        Assert.Equal(nameof(InvokeAgentActivity), activityName);
        Assert.NotNull(captured);
        Assert.Equal("alpha", captured!.AgentName);
        Assert.Equal("message", captured.Message);
        Assert.Equal("key", captured.ChatClientKey);
    }
}
