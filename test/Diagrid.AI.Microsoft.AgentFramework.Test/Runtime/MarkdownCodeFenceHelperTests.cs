using System.Reflection;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class MarkdownCodeFenceHelperTests
{
    private static readonly Type HelperType = typeof(WorkflowContextExtensions).Assembly
        .GetType("Diagrid.AI.Microsoft.AgentFramework.Runtime.MarkdownCodeFenceHelper", throwOnError: true)!;

    private static string Strip(string text)
    {
        var method = HelperType.GetMethod("StripCodeFenceIfPresent", BindingFlags.Static | BindingFlags.NonPublic);
        return (string)method!.Invoke(null, [text, NullLogger.Instance])!;
    }

    private static string Extract(string text)
    {
        var method = HelperType.GetMethod("ExtractJsonPayload", BindingFlags.Static | BindingFlags.NonPublic);
        return (string)method!.Invoke(null, [text, NullLogger.Instance])!;
    }

    [Fact]
    public void StripCodeFenceIfPresent_RemovesFence()
    {
        var input = "```json\n{\"value\":\"ok\"}\n```";

        var result = Strip(input);

        Assert.Equal("{\"value\":\"ok\"}\n", result);
    }

    [Fact]
    public void StripCodeFenceIfPresent_ReturnsOriginalWhenNoFence()
    {
        var input = "{\"value\":\"ok\"}";

        var result = Strip(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ExtractJsonPayload_TrimsAndExtractsJson()
    {
        var input = "Here you go:\n```json\n{\"value\":\"ok\"}\n```";

        var result = Extract(input);

        Assert.Equal("{\"value\":\"ok\"}", result);
    }

    [Fact]
    public void ExtractJsonPayload_ReturnsTrimmedWhenNoJson()
    {
        var input = "  no json here ";

        var result = Extract(input);

        Assert.Equal("no json here", result);
    }

    [Fact]
    public void ExtractJsonPayload_HandlesArray()
    {
        var input = "prefix [1,2,3] suffix";

        var result = Extract(input);

        Assert.Equal("[1,2,3]", result);
    }
}
