using Diagrid.AI.Microsoft.AgentFramework.Runtime;
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

        var context = new TestWorkflowContext("instance-1", (name, input) =>
        {
            activityName = name;
            captured = (DaprAgentInvocation)input!;
            return Task.FromResult<object?>(AgentRunResponseFactory.CreateWithText("{}"));
        });

        var agent = context.GetAgent("alpha", "key");
        var response = await context.RunAgentAsync(agent, message: "hello");

        Assert.Equal(nameof(AgentRunWorkflow), activityName);
        Assert.NotNull(captured);
        Assert.Equal("alpha", captured!.AgentName);
        Assert.Equal("hello", captured.Message);
        Assert.Equal("key", captured.ChatClientKey);
        Assert.NotNull(response);
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
            return Task.FromResult<object?>(AgentRunResponseFactory.CreateWithText(text));
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
            return Task.FromResult<object?>(AgentRunResponseFactory.CreateWithText(" "));
        });

        var agent = context.GetAgent("gamma");
        var result = await context.RunAgentAndDeserializeAsync<TestPayload>(agent, NullLogger.Instance);

        Assert.Null(result);
    }
}
