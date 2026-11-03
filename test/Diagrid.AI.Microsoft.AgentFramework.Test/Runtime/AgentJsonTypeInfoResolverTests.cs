using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentJsonTypeInfoResolverTests
{
    [Fact]
    public void GetTypeInfo_HonorsContextNamingPolicy()
    {
        var resolver = new AgentJsonTypeInfoResolver([CamelCaseJsonContext.Default]);

        var typeInfo = resolver.GetTypeInfo<CamelCasePayload>();
        Assert.NotNull(typeInfo);

        var result = JsonSerializer.Deserialize("""{"answer":"Paris","confidence":0.95}""", typeInfo);

        Assert.NotNull(result);
        Assert.Equal("Paris", result.Answer);
        Assert.Equal(0.95, result.Confidence, precision: 5);
    }

    [Fact]
    public void GetTypeInfo_ResolvesTypeFromLaterContext()
    {
        var resolver = new AgentJsonTypeInfoResolver([TestJsonContext.Default, CamelCaseJsonContext.Default]);

        var typeInfo = resolver.GetTypeInfo<CamelCasePayload>();

        Assert.NotNull(typeInfo);
        Assert.Same(CamelCaseJsonContext.Default.Options, typeInfo.Options);
    }

    [Fact]
    public void GetTypeInfo_ReturnsNull_WhenNoContextRegistersType()
    {
        var resolver = new AgentJsonTypeInfoResolver([TestJsonContext.Default]);

        Assert.Null(resolver.GetTypeInfo<CamelCasePayload>());
    }

    [Fact]
    public void GetTypeInfo_ReturnsNull_WhenNoContexts()
    {
        var resolver = new AgentJsonTypeInfoResolver([]);

        Assert.Null(resolver.GetTypeInfo<TestPayload>());
    }
}
