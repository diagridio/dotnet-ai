using System.Text.Json;
using Dapr.Common.Serialization;
using Dapr.Workflow.Serialization;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
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

    [Fact]
    public void AddDaprAgents_ConfiguresWorkflowJsonSerializerOptions()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents(options => options.UseJsonSerializerOptions(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IDaprSerializer>();

        var json = serializer.Serialize(new SerializerTestModel("Ada"));

        Assert.Contains("firstName", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstName", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDaprAgents_ConfiguresWorkflowSerializerInstance()
    {
        var services = new ServiceCollection();
        var serializer = new TestDaprSerializer("instance");

        services.AddDaprAgents(options => options.UseSerializer(serializer));

        var provider = services.BuildServiceProvider();

        Assert.Same(serializer, provider.GetRequiredService<IDaprSerializer>());
    }

    [Fact]
    public void AddDaprAgents_ConfiguresWorkflowSerializerFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SerializerMarker("factory"));

        services.AddDaprAgents(options => options.UseSerializer(sp =>
            new TestWorkflowSerializer(sp.GetRequiredService<SerializerMarker>().Value)));

        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IDaprSerializer>();

        Assert.Equal("factory", serializer.Serialize(new SerializerTestModel("Ada")));
    }

    [Fact]
    public void AddDaprAgents_LastConfiguredWorkflowSerializerWins()
    {
        var services = new ServiceCollection();
        var serializer = new TestDaprSerializer("custom");

        services.AddDaprAgents(options => options
            .UseJsonSerializerOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            .UseSerializer(serializer));

        var provider = services.BuildServiceProvider();

        Assert.Same(serializer, provider.GetRequiredService<IDaprSerializer>());
    }

    private sealed record SerializerTestModel(string FirstName);

    private sealed record SerializerMarker(string Value);

    private class TestDaprSerializer(string value) : IDaprSerializer
    {
        private readonly string _value = value;

        public string Serialize<T>(T value) => _value;

        public string Serialize(object? value, Type? inputType) => _value;

        public T? Deserialize<T>(string? data) => default;

        public object? Deserialize(string? data, Type returnType) => null;
    }

    private sealed class TestWorkflowSerializer(string value) : TestDaprSerializer(value), IWorkflowSerializer;
}
