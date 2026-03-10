using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

[Collection("AgentJsonResolver")]
public sealed class AgentJsonResolverAccessorTests
{
    [Fact]
    public void Resolver_ThrowsWhenUninitialized()
    {
        AgentJsonResolverTestHelper.Reset();

        Assert.Throws<InvalidOperationException>(() => _ = AgentJsonResolverAccessor.Resolver);
    }

    [Fact]
    public void Initialize_SetsResolverOnce()
    {
        AgentJsonResolverTestHelper.Reset();
        var first = new AgentJsonResolverTestHelper.StubResolver(TestJsonContext.Default.TestPayload);
        var second = new AgentJsonResolverTestHelper.StubResolver(TestJsonContext.Default.TestPayload);

        AgentJsonResolverAccessor.Initialize(first);
        AgentJsonResolverAccessor.Initialize(second);

        Assert.Same(first, AgentJsonResolverAccessor.Resolver);
    }
}
