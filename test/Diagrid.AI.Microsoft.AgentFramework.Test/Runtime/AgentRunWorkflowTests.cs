using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentRunWorkflowTests
{
    [Fact]
    public async Task RunAsync_CallsLlmActivity_AndReturnsFinalResponse()
    {
        string? activityName = null;

        // The workflow calls CallActivityAsync<CallLlmOutput>("CallLlmActivity", ...).
        // CallLlmOutput is internal, so we build the return value via reflection.
        var callLlmOutputType = typeof(AgentRunWorkflow).Assembly.GetType(
            "Diagrid.AI.Microsoft.AgentFramework.Runtime.CallLlmOutput")!;
        var output = Activator.CreateInstance(callLlmOutputType)!;
        callLlmOutputType.GetProperty("IsFinal")!.SetValue(output, true);
        callLlmOutputType.GetProperty("Text")!.SetValue(output, "done");

        var context = new TestWorkflowContext("workflow-1", (name, _) =>
        {
            activityName = name;
            return Task.FromResult<object?>(output);
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "message", null, null) { ChatClientKey = "key" };

        var result = await workflow.RunAsync(context, invocation);

        Assert.NotNull(result);
        Assert.Equal("CallLlmActivity", activityName);
    }
}
