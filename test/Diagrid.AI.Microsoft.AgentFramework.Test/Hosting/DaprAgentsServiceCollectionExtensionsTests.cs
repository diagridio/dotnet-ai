using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDaprAgents_RegistersCoreServices()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<AgentRegistry>());
        Assert.NotNull(provider.GetService<IDaprAgentInvoker>());
        Assert.NotNull(provider.GetService<IDaprAgentContextAccessor>());
    }

    [Fact]
    public void AddDaprAgents_RegistersSerializationServicesWhenConfigured()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents(options => options.AddContext(TestJsonContext.Default));

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetService<IAgentJsonTypeInfoResolver>();
        Assert.NotNull(resolver);

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, service => service.GetType().Name == "AgentJsonResolverInitializer");
    }

    [Fact]
    public void AddDaprAgents_DoesNotRegisterSerializationServicesByDefault()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents();

        var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IAgentJsonTypeInfoResolver>());
    }
}
