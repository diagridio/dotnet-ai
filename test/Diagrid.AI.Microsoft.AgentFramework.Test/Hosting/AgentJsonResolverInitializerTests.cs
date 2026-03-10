using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

[Collection("AgentJsonResolver")]
public sealed class AgentJsonResolverInitializerTests
{
    [Fact]
    public async Task StartAsync_InitializesResolverWhenRegistered()
    {
        AgentJsonResolverTestHelper.Reset();
        var services = new ServiceCollection();
        services.AddDaprAgents(options => options.AddContext(TestJsonContext.Default));
        var provider = services.BuildServiceProvider();

        var initializer = provider.GetServices<IHostedService>()
            .First(service => service.GetType().Name == "AgentJsonResolverInitializer");

        await initializer.StartAsync(CancellationToken.None);

        Assert.NotNull(AgentJsonResolverAccessor.Resolver);
    }
}
