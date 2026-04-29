using Diagrid.AI.Microsoft.AgentFramework.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentContextTests
{
    [Fact]
    public void CurrentWorkflowInstanceId_IsNull_WhenNotProvided()
    {
        var context = new DaprAgentContext(null!);

        Assert.Null(context.CurrentWorkflowInstanceId);
    }

    [Fact]
    public void CurrentWorkflowInstanceId_ReturnsProvidedValue()
    {
        const string instanceId = "wf-instance-42";

        var context = new DaprAgentContext(null!, currentWorkflowInstanceId: instanceId);

        Assert.Equal(instanceId, context.CurrentWorkflowInstanceId);
    }

    [Fact]
    public void CurrentWorkflowInstanceId_IsReadOnly()
    {
        var prop = typeof(DaprAgentContext).GetProperty(nameof(DaprAgentContext.CurrentWorkflowInstanceId));

        Assert.NotNull(prop);
        Assert.False(prop!.CanWrite, "CurrentWorkflowInstanceId should be read-only.");
    }
}
