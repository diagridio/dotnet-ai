using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

public sealed class DaprAgentsSerializationOptionsTests
{
    [Fact]
    public void AddContext_AddsProvidedContext()
    {
        var options = new DaprAgentsSerializationOptions();

        options.AddContext(TestJsonContext.Default);

        Assert.Single(GetContexts(options));
    }

    [Fact]
    public void AddContext_WithFactory_AddsContext()
    {
        var options = new DaprAgentsSerializationOptions();

        options.AddContext(() => TestJsonContext.Default);

        Assert.Single(GetContexts(options));
    }

    [Fact]
    public void AddContext_ThrowsOnNull()
    {
        var options = new DaprAgentsSerializationOptions();

        Assert.Throws<ArgumentNullException>(() => options.AddContext(null!));
        Assert.Throws<ArgumentNullException>(() => options.AddContext<TestJsonContext>(null!));
    }

    private static IReadOnlyCollection<object> GetContexts(DaprAgentsSerializationOptions options)
    {
        var prop = typeof(DaprAgentsSerializationOptions).GetProperty("Contexts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (IReadOnlyCollection<object>)prop.GetValue(options)!;
    }
}
