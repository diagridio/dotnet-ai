using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentRunWorkflowTests
{
    [Fact]
    public async Task RunAsync_CallsLlmActivity_AndReturnsFinalResponse()
    {
        string? activityName = null;

        // CallLlmOutput is now accessible via InternalsVisibleTo — no reflection needed.
        var output = new CallLlmOutput
        {
            IsFinal = true,
            Text = "done"
        };

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

    [Fact]
    public async Task RunAsync_WithToolCalls_ExecutesToolsAndLoops()
    {
        var callCount = 0;

        var context = new TestWorkflowContext("workflow-2", (name, _) =>
        {
            callCount++;
            if (name == "CallLlmActivity" && callCount == 1)
            {
                // First LLM call returns a tool call
                return Task.FromResult<object?>(new CallLlmOutput
                {
                    IsFinal = false,
                    Text = null,
                    FunctionCalls =
                    [
                        new WorkflowFunctionCall
                        {
                            CallId = "call-1",
                            Name = "my_tool",
                            ArgumentsJson = "{\"arg\":\"val\"}"
                        }
                    ]
                });
            }

            if (name == "ExecuteToolActivity")
            {
                // Tool execution returns a result
                return Task.FromResult<object?>(new ExecuteToolOutput
                {
                    CallId = "call-1",
                    FunctionName = "my_tool",
                    ResultJson = "\"tool result\""
                });
            }

            // Second LLM call returns final response
            return Task.FromResult<object?>(new CallLlmOutput
            {
                IsFinal = true,
                Text = "final answer"
            });
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "do something", null, null);

        var result = await workflow.RunAsync(context, invocation);

        Assert.NotNull(result);
        Assert.Equal("final answer", result.Text);
        Assert.Equal(3, callCount); // LLM -> Tool -> LLM
    }

    [Fact]
    public async Task RunAsync_NullMessage_Throws()
    {
        var context = new TestWorkflowContext("workflow-3", (_, _) =>
            Task.FromResult<object?>(null));

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", null, null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => workflow.RunAsync(context, invocation));
    }

    [Fact]
    public async Task RunAsync_WhitespaceMessage_Throws()
    {
        var context = new TestWorkflowContext("workflow-4", (_, _) =>
            Task.FromResult<object?>(null));

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "   ", null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => workflow.RunAsync(context, invocation));
    }
}
