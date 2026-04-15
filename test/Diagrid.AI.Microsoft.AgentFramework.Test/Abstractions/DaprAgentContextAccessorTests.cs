using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Abstractions;

public sealed class DaprAgentContextAccessorTests
{
    [Fact]
    public async Task Current_FlowsAcrossAsyncCalls()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents();
        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IDaprAgentContextAccessor>();
        var context = new DaprAgentContext(null!);
        accessor.Current = context;

        var value = await Task.Run(() => accessor.Current);

        Assert.Same(context, value);
    }

    [Fact]
    public void Current_CanBeCleared()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents();
        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IDaprAgentContextAccessor>();
        accessor.Current = new DaprAgentContext(null!);

        accessor.Current = null;

        Assert.Null(accessor.Current);
    }

    /// <summary>
    /// Proves that concurrent async flows each see their own value of
    /// <see cref="IDaprAgentContextAccessor.Current"/>, even though the accessor
    /// is a singleton. This validates that <see cref="AsyncLocal{T}"/> provides
    /// per-flow isolation — the same guarantee that makes ASP.NET Core's
    /// <c>IHttpContextAccessor</c> safe as a singleton.
    /// </summary>
    [Fact]
    public async Task Current_ParallelAsyncFlows_DoNotInterfere()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents();
        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IDaprAgentContextAccessor>();

        const int parallelism = 20;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, parallelism).Select(i => Task.Run(async () =>
        {
            // Each async flow sets its own unique context.
            var ctx = new DaprAgentContext(null!, currentWorkflowInstanceId: $"wf-{i}");
            accessor.Current = ctx;

            // Wait for the gate so all flows overlap when reading.
            await gate.Task;

            // Each flow must still see its own value, not another flow's.
            var actual = accessor.Current;
            if (actual is null || actual.CurrentWorkflowInstanceId != $"wf-{i}")
            {
                errors.Add(
                    $"Flow {i}: expected 'wf-{i}' but got '{actual?.CurrentWorkflowInstanceId ?? "null"}'");
            }

            // Clean up, mirroring ExecuteToolActivity's finally block.
            accessor.Current = null;
        }));

        // Open the gate once all tasks are queued.
        gate.SetResult();

        await Task.WhenAll(tasks);

        Assert.Empty(errors);
    }
}
