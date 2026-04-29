using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderExtensionsTests
{
    // =========================================================================
    // WithAgent(builder, AgentFactoryRegistration)
    // =========================================================================

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
        Assert.Contains(registrations, sd =>
            sd.ImplementationInstance is AgentFactoryRegistration r &&
            r.Name == registration.Name &&
            r.ChatClientKey == registration.ChatClientKey);
    }

    [Fact]
    public void WithAgent_ExplicitRegistration_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("alpha"));

        Assert.Throws<ArgumentNullException>(() => builder.WithAgent(registration));
    }

    [Fact]
    public void WithAgent_ExplicitRegistration_NullRegistration_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() => builder.WithAgent((AgentFactoryRegistration)null!));
    }

    [Fact]
    public void WithAgent_ThrowsForUnsupportedBuilder()
    {
        var builder = new FakeBuilder();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("alpha"));

        Assert.Throws<InvalidOperationException>(() => builder.WithAgent(registration));
    }

    // =========================================================================
    // WithAgent(builder, conversationComponentName, Func<IChatClient, AIAgent>)
    //
    // NOTE: IAgentsBuilder declares WithAgent(string, Func<IServiceProvider, AIAgent>),
    // which the compiler prefers over the extension method when the builder variable is
    // typed as IAgentsBuilder and the lambda is compatible with both delegate types.
    // Tests for this overload use an explicitly-typed Func<IChatClient, AIAgent> local so
    // the compiler unambiguously resolves to the extension method.
    // =========================================================================

    [Fact]
    public void WithAgent_ComponentNameFactory_NullBuilder_Throws()
    {
        Func<IChatClient, AIAgent> factory = _ => new TestAIAgent("a");

        Assert.Throws<ArgumentNullException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(null!, "component", factory));
    }

    [Fact]
    public void WithAgent_ComponentNameFactory_NullComponentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();
        Func<IChatClient, AIAgent> factory = _ => new TestAIAgent("a");

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent(null!, factory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_ComponentNameFactory_WhitespaceComponentName_Throws(string componentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();
        Func<IChatClient, AIAgent> factory = _ => new TestAIAgent("a");

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent(componentName, factory));
    }

    [Fact]
    public void WithAgent_ComponentNameFactory_NullFactory_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("component", (Func<IChatClient, AIAgent>)null!));
    }

    [Fact]
    public void WithAgent_ComponentNameFactory_UnsupportedBuilder_Throws()
    {
        Func<IChatClient, AIAgent> factory = _ => new TestAIAgent("a");

        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "component", factory));
    }

    [Fact]
    public void WithAgent_ComponentNameFactory_AddsRegistration()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        Func<IChatClient, AIAgent> factory = _ => new TestAIAgent("a");

        builder.WithAgent("my-component", factory);

        var registrations = services.Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration)).ToList();
        Assert.True(registrations.Count >= 1);
    }

    // =========================================================================
    // WithAgent(builder, agentName, conversationComponentName, Func<IChatClient, AIAgent>)
    // =========================================================================

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "component", (_) => new TestAIAgent("a")));
    }

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent(null!, "component", (_) => new TestAIAgent("a")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameComponentNameFactory_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent(agentName, "component", (_) => new TestAIAgent("a")));
    }

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_NullComponentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", null!, (_) => new TestAIAgent("a")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameComponentNameFactory_WhitespaceComponentName_Throws(string componentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent("agent", componentName, (_) => new TestAIAgent("a")));
    }

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_NullFactory_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "component", (Func<IChatClient, AIAgent>)null!));
    }

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_SetsNameAndChatClientKey()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("my-agent", "my-component", (_) => new TestAIAgent("my-agent"));

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "my-agent" && r.ChatClientKey == "my-component");
    }

    // =========================================================================
    // WithAgent(builder, agentName, instructions) — uses default IChatClient
    // =========================================================================

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
    public void WithAgent_AgentNameInstructions_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithAgent("agent", "instructions"));
    }

    [Fact]
    public void WithAgent_AgentNameInstructions_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => builder.WithAgent(null!, "instructions"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameInstructions_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithAgent(agentName, "instructions"));
    }

    [Fact]
    public void WithAgent_AgentNameInstructions_NullInstructions_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => builder.WithAgent("agent", (string)null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameInstructions_WhitespaceInstructions_Throws(string instructions)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithAgent("agent", instructions));
    }

    [Fact]
    public void WithAgent_AgentNameInstructions_AddsRegistrationWithAgentName()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("my-agent", "Do something useful.");

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "my-agent");
    }

    // =========================================================================
    // WithAgent(builder, agentName, instructions, chatClientKey, description)
    // =========================================================================

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "instructions", "key", description: null));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent(null!, "instructions", "key", description: null));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_NullInstructions_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", (string)null!, "key", description: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameInstructionsChatClientKey_WhitespaceKey_Throws(string key)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent("agent", "instructions", key, description: null));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_AddsRegistrationWithAgentName()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("named-agent", "Be helpful.", "some-key", description: "A helpful agent.");

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "named-agent");
    }

    // =========================================================================
    // WithAgent(builder, agentName, instructions, IChatClient, description?)
    // =========================================================================

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClient_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;
        var chatClient = new Mock<IChatClient>().Object;

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "instructions", chatClient));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClient_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent(null!, "instructions", chatClient));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameInstructionsChatClient_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent(agentName, "instructions", chatClient));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClient_NullInstructions_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", null!, chatClient));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_AgentNameInstructionsChatClient_WhitespaceInstructions_Throws(string instructions)
    {
        var builder = new ServiceCollection().AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        Assert.Throws<ArgumentException>(() =>
            builder.WithAgent("agent", instructions, chatClient));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClient_AddsRegistrationWithAgentName()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        builder.WithAgent("inline-agent", "Be concise.", chatClient);

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "inline-agent");
    }

    // =========================================================================
    // WithAgent(builder, agentName, conversationComponentName, instructions, description?)
    //
    // NOTE: This overload has 3 required string params + optional params. Using
    // 3 positional strings forces the compiler to pick this overload rather than
    // the 4-required-string overload (agentName, instructions, chatClientKey, description).
    // =========================================================================

    [Fact]
    public void WithAgent_ComponentNameInstructions_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "component", "instructions"));
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_NullAgentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent((string)null!, "component", "instructions"));
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_NullComponentName_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", (string)null!, "instructions"));
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_NullInstructions_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "component", (string)null!));
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_SetsNameAndChatClientKey()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        // 3 positional strings → (agentName, conversationComponentName, instructions)
        // which sets ChatClientKey = conversationComponentName
        builder.WithAgent("my-agent", "my-component", "Be helpful.");

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "my-agent" && r.ChatClientKey == "my-component");
    }

    // =========================================================================
    // WithAgent(..., IReadOnlyList<AITool> tools)
    // =========================================================================

    [Fact]
    public void WithAgent_WithTools_NullTools_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithAgent("agent", "component", "instructions", (IReadOnlyList<AITool>)null!));
    }

    [Fact]
    public void WithAgent_WithTools_EmptyToolList_AddsRegistration()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("agent", "component", "instructions", tools: []);

        var registrations = services.Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration)).ToList();
        Assert.True(registrations.Count >= 1);
    }

    [Fact]
    public void WithAgent_WithDescriptionAndTools_SetsNameAndChatClientKey()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        builder.WithAgent("agent", "component", "instructions", description: null, tools: []);

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Name == "agent" && r.ChatClientKey == "component");
    }

    // =========================================================================
    // Argument-validation: ThrowsOnInvalidParameters (retained from original)
    // =========================================================================

    [Fact]
    public void WithAgent_ThrowsOnInvalidParameters()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithAgent("", "component", "instructions"));
        Assert.Throws<ArgumentException>(() => builder.WithAgent("agent", "", "instructions"));
        Assert.Throws<ArgumentException>(() => builder.WithAgent("agent", "component", ""));
    }

    // =========================================================================
    // FakeBuilder — unsupported builder throws for overloads that call GetServices
    // =========================================================================

    [Fact]
    public void WithAgent_AgentNameComponentNameFactory_UnsupportedBuilder_Throws()
    {
        // This overload calls GetServices which throws for non-DaprAgentsBuilder
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "component", (_) => new TestAIAgent("a")));
    }

    [Fact]
    public void WithAgent_AgentNameInstructions_UnsupportedBuilder_Throws()
    {
        // WithAgent(agentName, instructions) calls builder.WithAgent(registration) which throws
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "Be helpful."));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_UnsupportedBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "instructions", "key", description: null));
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientInstance_UnsupportedBuilder_Throws()
    {
        var chatClient = new Mock<IChatClient>().Object;

        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "instructions", chatClient));
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_UnsupportedBuilder_Throws()
    {
        // 3-string overload (agentName, componentName, instructions) calls GetServices → throws
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "component", "instructions"));
    }

    [Fact]
    public void WithAgent_WithTools_UnsupportedBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "component", "instructions",
                (IReadOnlyList<AITool>)[]));
    }

    [Fact]
    public void WithAgent_WithDescriptionAndTools_UnsupportedBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaprAgentsBuilderExtensions.WithAgent(new FakeBuilder(), "agent", "component", "instructions",
                description: null, (IReadOnlyList<AITool>)[]));
    }

    // =========================================================================
    // Return-value (fluent interface)
    // =========================================================================

    [Fact]
    public void WithAgent_ExplicitRegistration_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("a")) { Name = "a" };

        var result = builder.WithAgent(registration);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_AgentNameInstructions_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithAgent("my-agent", "Do something.");

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClientKey_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithAgent("agent", "instructions", "key", description: null);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_AgentNameInstructionsChatClient_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();
        var chatClient = new Mock<IChatClient>().Object;

        var result = builder.WithAgent("agent", "instructions", chatClient);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_ComponentNameInstructions_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithAgent("agent", "component", "instructions");

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_WithTools_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithAgent("agent", "component", "instructions", tools: []);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithAgent_WithDescriptionAndTools_ReturnsBuilderInstance()
    {
        var services = new ServiceCollection();
        var builder = services.AddDaprAgents();

        var result = builder.WithAgent("agent", "component", "instructions", description: "desc", tools: []);

        Assert.Same(builder, result);
    }

    // =========================================================================
    // FakeBuilder helper
    // =========================================================================

    private sealed class FakeBuilder : IAgentsBuilder
    {
        public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory) => this;

        public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory) => this;
    }
}
