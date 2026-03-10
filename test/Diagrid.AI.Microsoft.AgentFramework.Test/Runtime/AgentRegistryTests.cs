using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

public sealed class AgentRegistryTests
{
    [Fact]
    public void AddFactory_WithExplicitName_IsLazyAndResolves()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);
        var calls = 0;

        registry.AddFactory(_ =>
        {
            calls++;
            return new TestAIAgent("alpha");
        }, chatClientKey: null, agentName: "alpha", provider);

        Assert.Equal(0, calls);

        var agent = registry.Get("alpha", provider);
        Assert.Equal("alpha", agent.Name);
        Assert.Equal(1, calls);

        var agentAgain = registry.Get("alpha", provider);
        Assert.Same(agent, agentAgain);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AddFactory_WithoutName_ResolvesFromPending()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);
        var calls = 0;

        registry.AddFactory(_ =>
        {
            calls++;
            return new TestAIAgent("beta");
        }, chatClientKey: "chat", agentName: null, provider);

        var agent = registry.Get("beta", "chat", provider);
        Assert.Equal("beta", agent.Name);
        Assert.Equal(1, calls);

        var agentAgain = registry.Get("beta", "chat", provider);
        Assert.Same(agent, agentAgain);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Get_WithoutChatClientKey_ThrowsOnAmbiguousName()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        registry.AddFactory(_ => new TestAIAgent("gamma"), "a", "gamma", provider);
        registry.AddFactory(_ => new TestAIAgent("gamma"), "b", "gamma", provider);

        Assert.Throws<InvalidOperationException>(() => registry.Get("gamma", provider));
    }

    [Fact]
    public void RegisteredNames_ReturnsDistinctNames()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        registry.AddFactory(_ => new TestAIAgent("Delta"), "k1", "Delta", provider);
        registry.AddFactory(_ => new TestAIAgent("delta"), "k2", "delta", provider);

        var names = registry.RegisteredNames.ToList();
        Assert.Single(names);
        Assert.Equal("Delta", names[0], ignoreCase: true);
    }

    [Fact]
    public void Get_ThrowsWhenNotRegistered()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Get("missing", provider));
        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FactoryReturningNull_Throws()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        registry.AddFactory(_ => null!, chatClientKey: null, agentName: "null", provider);

        Assert.Throws<InvalidOperationException>(() => registry.Get("null", provider));
    }

    [Fact]
    public void FactoryReturningEmptyName_Throws()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        registry.AddFactory(_ => new TestAIAgent(""), chatClientKey: null, agentName: "bad", provider);

        Assert.Throws<InvalidOperationException>(() => registry.Get("bad", provider));
    }

    [Fact]
    public void AddFactory_WithNullFactory_Throws()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var registry = new AgentRegistry(provider, []);

        Assert.Throws<ArgumentNullException>(() => registry.AddFactory(null!, null, null, provider));
    }

}
