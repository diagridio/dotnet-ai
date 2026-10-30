using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

[Collection("AgentJsonResolver")]
public sealed class WorkflowContextExtensionsTests
{
    [Fact]
    public async Task RunAgentAsync_PassesInvocationToActivity()
    {
        DaprAgentInvocation? captured = null;
        string? activityName = null;
        var options = new AgentRunOptions();

        var context = new TestWorkflowContext("instance-1", (name, input) =>
        {
            activityName = name;
            captured = (DaprAgentInvocation)input!;
            return Task.FromResult<object?>(new AgentRunResult { Response = AgentRunResponseFactory.CreateWithText("{}") });
        });

        var agent = context.GetAgent("alpha", "key");
        var response = await context.RunAgentAsync(agent, message: "hello", options: options);

        Assert.Equal(nameof(AgentRunWorkflow), activityName);
        Assert.NotNull(captured);
        Assert.Equal("alpha", captured!.AgentName);
        Assert.Equal("hello", captured.Message);
        Assert.Equal("key", captured.ChatClientKey);
        Assert.Same(options, captured.Options);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task StartAgentRunDetachedAsync_SchedulesAgentRunWorkflowWithoutWaiting()
    {
        var activityCalled = false;
        var options = new AgentRunOptions();
        var context = new TestWorkflowContext("instance-d", (_, _) =>
        {
            activityCalled = true;
            return Task.FromResult<object?>(null);
        });

        var agent = context.GetAgent("alpha", "key");
        var instanceId = await context.StartAgentRunDetachedAsync(agent, message: "hello", options: options,
            instanceId: "run-1", telemetryBaggage: new Dictionary<string, string?> { ["tenant.id"] = "t1" });

        Assert.Equal("run-1", instanceId);
        Assert.False(activityCalled);
        var (workflowName, scheduledId, input) = Assert.Single(context.DetachedWorkflows);
        Assert.Equal(nameof(AgentRunWorkflow), workflowName);
        Assert.Equal("run-1", scheduledId);
        var invocation = Assert.IsType<DaprAgentInvocation>(input);
        Assert.Equal("alpha", invocation.AgentName);
        Assert.Equal("hello", invocation.Message);
        Assert.Equal("key", invocation.ChatClientKey);
        Assert.Null(invocation.Session);
        Assert.Same(options, invocation.Options);
        Assert.Equal("t1", invocation.TelemetryBaggage!["tenant.id"]);
    }

    [Fact]
    public async Task StartAgentRunDetachedAsync_NoInstanceId_ReturnsGeneratedId()
    {
        var context = new TestWorkflowContext("instance-e", (_, _) => Task.FromResult<object?>(null));

        var instanceId = await context.StartAgentRunDetachedAsync(context.GetAgent("alpha"));

        Assert.False(string.IsNullOrEmpty(instanceId));
        Assert.Null(Assert.Single(context.DetachedWorkflows).InstanceId);
    }

    [Fact]
    public async Task StartAgentRunDetachedAsync_NullArguments_Throw()
    {
        var context = new TestWorkflowContext("instance-f", (_, _) => Task.FromResult<object?>(null));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((WorkflowContext)null!).StartAgentRunDetachedAsync(context.GetAgent("alpha")));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            context.StartAgentRunDetachedAsync(null!));
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_DeserializesJsonPayload()
    {
        AgentJsonResolverTestHelper.Reset();
        var resolver = new AgentJsonResolverTestHelper.StubResolver(TestJsonContext.Default.TestPayload);
        AgentJsonResolverAccessor.Initialize(resolver);

        var context = new TestWorkflowContext("instance-2", (_, _) =>
        {
            var text = "```json\n{\"Value\":\"ok\"}\n```";
            return Task.FromResult<object?>(new AgentRunResult { Response = AgentRunResponseFactory.CreateWithText(text) });
        });

        var agent = context.GetAgent("beta");
        var result = await context.RunAgentAndDeserializeAsync<TestPayload>(agent, NullLogger.Instance, message: "hi");

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Value);
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_ReturnsDefaultOnEmpty()
    {
        AgentJsonResolverTestHelper.Reset();
        var resolver = new AgentJsonResolverTestHelper.StubResolver(TestJsonContext.Default.TestPayload);
        AgentJsonResolverAccessor.Initialize(resolver);

        var context = new TestWorkflowContext("instance-3", (_, _) =>
        {
            return Task.FromResult<object?>(new AgentRunResult { Response = AgentRunResponseFactory.CreateWithText(" ") });
        });

        var agent = context.GetAgent("gamma");
        var result = await context.RunAgentAndDeserializeAsync<TestPayload>(agent, NullLogger.Instance);

        Assert.Null(result);
    }
}
