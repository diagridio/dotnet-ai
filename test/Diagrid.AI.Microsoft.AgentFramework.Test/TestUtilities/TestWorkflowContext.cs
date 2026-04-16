using Dapr.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

internal sealed class TestWorkflowContext : WorkflowContext
{
    private readonly Func<string, object?, Task<object?>> _activityHandler;

    public TestWorkflowContext(string instanceId, Func<string, object?, Task<object?>> activityHandler)
    {
        InstanceId = instanceId;
        _activityHandler = activityHandler;
    }

    public override string Name => "TestWorkflow";

    public override string InstanceId { get; }

    public override DateTime CurrentUtcDateTime => DateTime.UtcNow;

    public override bool IsReplaying => false;

    public override Guid NewGuid() => Guid.NewGuid();

    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override Task CreateTimer(TimeSpan delay, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override ILogger CreateReplaySafeLogger(string categoryName) => NullLogger.Instance;

    public override ILogger CreateReplaySafeLogger(Type categoryType) => NullLogger.Instance;

    public override ILogger<T> CreateReplaySafeLogger<T>() => NullLogger<T>.Instance;

    public override bool IsPatched(string patchId) => false;

    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = false)
    {
    }

    public override Task<T> CallActivityAsync<T>(string name, object? input = null,
        WorkflowTaskOptions? options = null)
    {
        return CallActivityAsyncCore<T>(name, input);
    }

    public override Task CallActivityAsync(string name, object? input = null, WorkflowTaskOptions? options = null)
    {
        return CallActivityAsyncCore<object?>(name, input);
    }

    public override Task<T> CallChildWorkflowAsync<T>(string workflowName, object? input = null,
        ChildWorkflowTaskOptions? options = null)
    {
        return CallActivityAsyncCore<T>(workflowName, input);
    }

    public override Task CallChildWorkflowAsync(string workflowName, object? input = null,
        ChildWorkflowTaskOptions? options = null)
    {
        return CallActivityAsyncCore<object?>(workflowName, input);
    }

    public override void SendEvent(string instanceId, string eventName, object? eventData = null)
    {
    }

    public override Task<T> WaitForExternalEventAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(T)!);
    }

    public override void SetCustomStatus(object? customStatus)
    {
    }

    private async Task<T> CallActivityAsyncCore<T>(string name, object? input)
    {
        var result = await _activityHandler(name, input).ConfigureAwait(false);
        return (T)result!;
    }
}
