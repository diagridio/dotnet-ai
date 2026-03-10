using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

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
}
