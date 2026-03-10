using Dapr.Workflow;
using Dapr.Workflow.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

internal sealed class TestWorkflowActivityContext : WorkflowActivityContext
{
    public TestWorkflowActivityContext(string instanceId, string identifier = "activity", string? taskExecutionKey = null)
    {
        InstanceId = instanceId;
        Identifier = new TaskIdentifier(identifier);
        TaskExecutionKey = taskExecutionKey ?? Guid.NewGuid().ToString("N");
    }

    public override TaskIdentifier Identifier { get; }

    public override string TaskExecutionKey { get; }

    public override string InstanceId { get; }
}
