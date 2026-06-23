using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentRunWorkflowTests
{
    [Fact]
    public async Task RunAsync_CallsLlmActivity_AndReturnsFinalResponse()
    {
        string? activityName = null;
        CallLlmInput? capturedInput = null;

        // CallLlmOutput is now accessible via InternalsVisibleTo — no reflection needed.
        var output = new CallLlmOutput
        {
            IsFinal = true,
            Text = "done"
        };

        var context = new TestWorkflowContext("workflow-1", (name, input) =>
        {
            activityName = name;
            capturedInput = (CallLlmInput)input!;
            return Task.FromResult<object?>(output);
        });

        var workflow = new AgentRunWorkflow();
        var options = new AgentRunOptions();
        var invocation = new DaprAgentInvocation("alpha", "message", null, options) { ChatClientKey = "key" };

        var result = await workflow.RunAsync(context, invocation);

        Assert.NotNull(result);
        Assert.Equal("CallLlmActivity", activityName);
        Assert.NotNull(capturedInput);
        Assert.Same(options, capturedInput!.Options);
    }

    [Fact]
    public async Task RunAsync_ForwardsTelemetryBaggageToLlmActivity()
    {
        CallLlmInput? capturedInput = null;
        var context = new TestWorkflowContext("workflow-baggage", (_, input) =>
        {
            capturedInput = (CallLlmInput)input!;
            return Task.FromResult<object?>(new CallLlmOutput
            {
                IsFinal = true,
                Text = "done"
            });
        });

        var workflow = new AgentRunWorkflow();
        var customBaggage = new Dictionary<string, string?> { ["tenant.id"] = "tenant-1" };
        var invocation = new DaprAgentInvocation("alpha", "message", null, null)
        {
            ChatClientKey = "key",
            TelemetryBaggage = customBaggage
        };

        await workflow.RunAsync(context, invocation);

        Assert.NotNull(capturedInput);
        Assert.Same(customBaggage, capturedInput!.TelemetryBaggage);
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
        Assert.Equal("final answer", result.Response.Text);
        Assert.Equal(3, callCount); // LLM -> Tool -> LLM
    }

    [Fact]
    public async Task RunAsync_WithToolCalls_FunctionCallIdMatchesToolResultCallId()
    {
        // Regression guard: the CallId on the assistant's FunctionCall must be the same CallId
        // that appears on the subsequent tool-result WorkflowChatMessage.
        // Without this linkage, DaprChatClient (and OpenAI directly) reject the conversation
        // because the tool message has no matching tool_calls predecessor.
        string? capturedFunctionCallId = null;
        string? capturedToolResultCallId = null;

        var callCount = 0;

        var context = new TestWorkflowContext("workflow-link-1", (name, input) =>
        {
            callCount++;
            if (name == "CallLlmActivity" && callCount == 1)
            {
                return Task.FromResult<object?>(new CallLlmOutput
                {
                    IsFinal = false,
                    FunctionCalls = [new WorkflowFunctionCall { CallId = "link-call-1", Name = "fn", ArgumentsJson = "{}" }]
                });
            }

            if (name == "ExecuteToolActivity" && input is ExecuteToolInput toolInput)
            {
                capturedFunctionCallId = toolInput.CallId;
                return Task.FromResult<object?>(new ExecuteToolOutput
                {
                    CallId = toolInput.CallId,
                    FunctionName = toolInput.FunctionName,
                    ResultJson = "\"ok\""
                });
            }

            if (name == "CallLlmActivity" && input is CallLlmInput llmInput)
            {
                // Capture the CallId from the tool-result message that was built by the workflow.
                var toolMsg = llmInput.Messages.FirstOrDefault(m => m.Role == "tool");
                capturedToolResultCallId = toolMsg?.FunctionResults?.FirstOrDefault()?.CallId;

                return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "done" });
            }

            return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "done" });
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "run tool", null, null);

        await workflow.RunAsync(context, invocation);

        Assert.Equal("link-call-1", capturedFunctionCallId);
        Assert.Equal("link-call-1", capturedToolResultCallId);
        Assert.Equal(capturedFunctionCallId, capturedToolResultCallId);
    }

    [Fact]
    public async Task RunAsync_WithToolCalls_ForwardsTelemetryBaggageToToolActivity()
    {
        ExecuteToolInput? capturedInput = null;
        var customBaggage = new Dictionary<string, string?> { ["tenant.id"] = "tenant-1" };
        var callCount = 0;

        var context = new TestWorkflowContext("workflow-tool-baggage", (name, input) =>
        {
            callCount++;
            if (name == "CallLlmActivity" && callCount == 1)
            {
                return Task.FromResult<object?>(new CallLlmOutput
                {
                    IsFinal = false,
                    FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "fn", ArgumentsJson = "{}" }]
                });
            }

            if (name == "ExecuteToolActivity")
            {
                capturedInput = (ExecuteToolInput)input!;
                return Task.FromResult<object?>(new ExecuteToolOutput
                {
                    CallId = "c1",
                    FunctionName = "fn",
                    ResultJson = "\"ok\""
                });
            }

            return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "done" });
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "run tool", null, null)
        {
            TelemetryBaggage = customBaggage
        };

        await workflow.RunAsync(context, invocation);

        Assert.NotNull(capturedInput);
        Assert.Same(customBaggage, capturedInput!.TelemetryBaggage);
    }

    [Fact]
    public async Task RunAsync_WithToolCalls_TurnMessagesContainAssistantAndToolMessages()
    {
        var llmCallCount = 0;

        var context = new TestWorkflowContext("workflow-tm-1", (name, _) =>
        {
            if (name == "CallLlmActivity")
            {
                llmCallCount++;
                if (llmCallCount == 1)
                {
                    return Task.FromResult<object?>(new CallLlmOutput
                    {
                        IsFinal = false,
                        FunctionCalls = [new WorkflowFunctionCall { CallId = "c1", Name = "fn", ArgumentsJson = "{}" }]
                    });
                }

                // Second LLM call — final response.
                return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "final" });
            }

            if (name == "ExecuteToolActivity")
            {
                return Task.FromResult<object?>(new ExecuteToolOutput
                {
                    CallId = "c1",
                    FunctionName = "fn",
                    ResultJson = "\"r\""
                });
            }

            return Task.FromResult<object?>(new CallLlmOutput { IsFinal = true, Text = "final" });
        });

        var workflow = new AgentRunWorkflow();
        var invocation = new DaprAgentInvocation("alpha", "go", null, null);

        var result = await workflow.RunAsync(context, invocation);

        // TurnMessages should include: user, assistant(with FunctionCalls), tool(with FunctionResults), assistant(final).
        Assert.Contains(result.TurnMessages, m => m.Role == "user");
        Assert.Contains(result.TurnMessages, m => m.Role == "assistant" && m.FunctionCalls is { Count: > 0 });
        Assert.Contains(result.TurnMessages, m => m.Role == "tool" && m.FunctionResults is { Count: > 0 });
        Assert.Contains(result.TurnMessages, m => m.Role == "assistant" && m.Content == "final");
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
