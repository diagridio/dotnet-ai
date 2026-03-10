using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Tests;

public sealed class DaprAgentsBuilderExtensionsTests
{
    [Fact]
    public void WithAgent_ExplicitRegistration_AddsRegistration()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("alpha"))
        {
            Name = "alpha",
            ChatClientKey = "key"
        };

        builder.WithAgent(registration);

        var registrations = services.Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration)).ToList();
        Assert.Contains(registrations, sd => ReferenceEquals(sd.ImplementationInstance, registration));
    }

    [Fact]
    public void WithAgent_ThrowsForUnsupportedBuilder()
    {
        var builder = new FakeBuilder();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("alpha"));

        Assert.Throws<InvalidOperationException>(() => builder.WithAgent(registration));
    }

    [Fact]
    public void WithAgent_WithInstructions_AddsRegistration()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("agent", "component", "instructions");

        var registrations = services.Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration)).ToList();
        Assert.True(registrations.Count >= 1);
    }

    [Fact]
    public void WithAgent_ThrowsOnInvalidParameters()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithAgent("", "component", "instructions"));
        Assert.Throws<ArgumentException>(() => builder.WithAgent("agent", "", "instructions"));
        Assert.Throws<ArgumentException>(() => builder.WithAgent("agent", "component", ""));
    }

    private sealed class FakeBuilder : IAgentsBuilder
    {
        public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory) => this;

        public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory) => this;
    }
}
