using Dapr.Common.Serialization;
using Dapr.Workflow.Serialization;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using System.Text.Json;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Abstractions;

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

    [Fact]
    public void UseJsonSerializerOptions_SetsProvidedOptions()
    {
        var options = new DaprAgentsSerializationOptions();
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.UseJsonSerializerOptions(jsonSerializerOptions);

        Assert.Same(jsonSerializerOptions, GetJsonSerializerOptions(options));
    }

    [Fact]
    public void UseJsonSerializerOptions_ThrowsOnNull()
    {
        var options = new DaprAgentsSerializationOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseJsonSerializerOptions(null!));
    }

    [Fact]
    public void UseSerializer_WithSerializer_SetsProvidedSerializer()
    {
        var options = new DaprAgentsSerializationOptions();
        var serializer = new JsonDaprSerializer();

        options.UseSerializer(serializer);

        Assert.Same(serializer, GetWorkflowSerializer(options));
        Assert.Null(GetJsonSerializerOptions(options));
        Assert.Null(GetWorkflowSerializerFactory(options));
    }

    [Fact]
    public void UseSerializer_WithFactory_SetsProvidedFactory()
    {
        var options = new DaprAgentsSerializationOptions();
        Func<IServiceProvider, IWorkflowSerializer> factory = _ => new TestWorkflowSerializer();

        options.UseSerializer(factory);

        Assert.Same(factory, GetWorkflowSerializerFactory(options));
        Assert.Null(GetJsonSerializerOptions(options));
        Assert.Null(GetWorkflowSerializer(options));
    }

    [Fact]
    public void SerializerConfiguration_UsesLastConfiguredSerializer()
    {
        var options = new DaprAgentsSerializationOptions();
        var serializer = new JsonDaprSerializer();
        var jsonSerializerOptions = new JsonSerializerOptions();

        options.UseJsonSerializerOptions(jsonSerializerOptions).UseSerializer(serializer);

        Assert.Same(serializer, GetWorkflowSerializer(options));
        Assert.Null(GetJsonSerializerOptions(options));
    }

    [Fact]
    public void UseSerializer_ThrowsOnNull()
    {
        var options = new DaprAgentsSerializationOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseSerializer((IDaprSerializer)null!));
        Assert.Throws<ArgumentNullException>(() => options.UseSerializer((Func<IServiceProvider, IWorkflowSerializer>)null!));
    }

    private static IReadOnlyCollection<object> GetContexts(DaprAgentsSerializationOptions options)
    {
        var prop = typeof(DaprAgentsSerializationOptions).GetProperty("Contexts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (IReadOnlyCollection<object>)prop.GetValue(options)!;
    }

    private static JsonSerializerOptions? GetJsonSerializerOptions(DaprAgentsSerializationOptions options)
    {
        var prop = typeof(DaprAgentsSerializationOptions).GetProperty("JsonSerializerOptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (JsonSerializerOptions?)prop.GetValue(options);
    }

    private static IDaprSerializer? GetWorkflowSerializer(DaprAgentsSerializationOptions options)
    {
        var prop = typeof(DaprAgentsSerializationOptions).GetProperty("WorkflowSerializer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (IDaprSerializer?)prop.GetValue(options);
    }

    private static Func<IServiceProvider, IWorkflowSerializer>? GetWorkflowSerializerFactory(DaprAgentsSerializationOptions options)
    {
        var prop = typeof(DaprAgentsSerializationOptions).GetProperty("WorkflowSerializerFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (Func<IServiceProvider, IWorkflowSerializer>?)prop.GetValue(options);
    }

    private sealed class TestWorkflowSerializer : IWorkflowSerializer
    {
        private readonly JsonDaprSerializer _inner = new();

        public string Serialize<T>(T value) => _inner.Serialize(value);

        public string Serialize(object? value, Type? inputType) => _inner.Serialize(value, inputType);

        public T? Deserialize<T>(string? data) => _inner.Deserialize<T>(data);

        public object? Deserialize(string? data, Type returnType) => _inner.Deserialize(data, returnType);
    }
}
