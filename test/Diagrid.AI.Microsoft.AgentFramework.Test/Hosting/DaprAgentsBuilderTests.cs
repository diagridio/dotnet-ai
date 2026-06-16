using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Catalyst;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderTests
{
    // -------------------------------------------------------------------------
    // WithAgent(factory) overload
    // -------------------------------------------------------------------------

    [Fact]
    public void WithAgent_NullFactory_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAgent((Func<IServiceProvider, AIAgent>)null!));
    }

    [Fact]
    public void WithAgent_ValidFactory_AddsRegistrationWithNullChatClientKey()
    {
        var builder = BuildDaprBuilder();

        builder.WithAgent(_ => new TestAIAgent("alpha"));

        var registrations = FindRegistrations(builder.Services);
        Assert.Contains(registrations, r => r.ChatClientKey is null);
    }

    [Fact]
    public void WithAgent_ValidFactory_ReturnsBuilderInstance()
    {
        var builder = BuildDaprBuilder();

        var result = builder.WithAgent(_ => new TestAIAgent("alpha"));

        Assert.Same(builder, result);
    }

    // -------------------------------------------------------------------------
    // WithAgent(chatClientKey, factory) overload
    // -------------------------------------------------------------------------

    [Fact]
    public void WithAgent_WithKey_NullKey_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => builder.WithAgent(null!, _ => new TestAIAgent("alpha")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAgent_WithKey_WhitespaceKey_ThrowsArgumentException(string key)
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentException>(() => builder.WithAgent(key, _ => new TestAIAgent("alpha")));
    }

    [Fact]
    public void WithAgent_WithKey_NullFactory_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAgent("my-key", null!));
    }

    [Fact]
    public void WithAgent_WithKey_ValidArgs_AddsRegistrationWithCorrectKey()
    {
        var builder = BuildDaprBuilder();
        const string key = "my-chat-client";

        builder.WithAgent(key, _ => new TestAIAgent("alpha"));

        var registrations = FindRegistrations(builder.Services);
        Assert.Contains(registrations, r => r.ChatClientKey == key);
    }

    [Fact]
    public void WithAgent_WithKey_ValidArgs_ReturnsBuilderInstance()
    {
        var builder = BuildDaprBuilder();

        var result = builder.WithAgent("my-key", _ => new TestAIAgent("alpha"));

        Assert.Same(builder, result);
    }

    // -------------------------------------------------------------------------
    // WithAgentRegistration
    // -------------------------------------------------------------------------

    [Fact]
    public void WithAgentRegistration_NullRegistration_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAgentRegistration(null!));
    }

    [Fact]
    public void WithAgentRegistration_ValidRegistration_AddsSingletonToServices()
    {
        var builder = BuildDaprBuilder();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("beta")) { Name = "beta" };

        builder.WithAgentRegistration(registration);

        var descriptor = builder.Services
            .FirstOrDefault(sd =>
                sd.ServiceType == typeof(AgentFactoryRegistration) &&
                sd.ImplementationInstance is AgentFactoryRegistration r &&
                r.Name == "beta");

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void WithAgentRegistration_ReturnsBuilderInstance()
    {
        var builder = BuildDaprBuilder();
        var registration = new AgentFactoryRegistration(_ => new TestAIAgent("gamma"));

        var result = builder.WithAgentRegistration(registration);

        Assert.Same(builder, result);
    }

    // -------------------------------------------------------------------------
    // WithCatalyst
    // -------------------------------------------------------------------------

    [Fact]
    public void WithCatalyst_NullOptions_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithCatalyst(null!));
    }

    [Fact]
    public void WithCatalyst_NullRegistry_ThrowsArgumentNullException()
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithCatalyst(new DiagridCatalystOptions
        {
            Registry = null!,
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithCatalyst_WithoutStateStoreName_ThrowsArgumentException(string stateStoreName)
    {
        var builder = BuildDaprBuilder();

        Assert.Throws<ArgumentException>(() => builder.WithCatalyst(new DiagridCatalystOptions
        {
            Registry = new RegistryMetadata
            {
                ResourceName = stateStoreName,
            },
        }));
    }

    [Fact]
    public void WithCatalyst_ValidOptions_RegistersOptionsAndHostedService()
    {
        var builder = BuildDaprBuilder();
        var options = new DiagridCatalystOptions
        {
            Registry = new RegistryMetadata
            {
                ResourceName = "statestore",
            },
        };

        var result = builder.WithCatalyst(options);

        Assert.Same(builder, result);
        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(DiagridCatalystOptions) &&
            ReferenceEquals(descriptor.ImplementationInstance, options));
        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CatalystAgentRegistryHostedService));
    }

    // -------------------------------------------------------------------------
    // UnwrapFunctionInvoking
    // -------------------------------------------------------------------------

    [Fact]
    public void UnwrapFunctionInvoking_NonFunctionInvokingClient_ReturnsSameClient()
    {
        var mockClient = new Mock<IChatClient>().Object;

        var result = DaprAgentsBuilder.UnwrapFunctionInvoking(mockClient);

        Assert.Same(mockClient, result);
    }

    [Fact]
    public void UnwrapFunctionInvoking_SingleFunctionInvokingWrapper_ReturnsInnerClient()
    {
        var innerClient = new Mock<IChatClient>().Object;
        var wrapper = new FunctionInvokingChatClient(innerClient);

        var result = DaprAgentsBuilder.UnwrapFunctionInvoking(wrapper);

        Assert.Same(innerClient, result);
    }

    [Fact]
    public void UnwrapFunctionInvoking_NestedFunctionInvokingWrappers_ReturnsInnermostClient()
    {
        var innermost = new Mock<IChatClient>().Object;
        var inner = new FunctionInvokingChatClient(innermost);
        var outer = new FunctionInvokingChatClient(inner);

        var result = DaprAgentsBuilder.UnwrapFunctionInvoking(outer);

        Assert.Same(innermost, result);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DaprAgentsBuilder BuildDaprBuilder()
    {
        var services = new ServiceCollection();
        services.AddDaprAgents();
        return new DaprAgentsBuilder(services);
    }

    private static IReadOnlyList<AgentFactoryRegistration> FindRegistrations(IServiceCollection services) =>
        services
            .Where(sd => sd.ServiceType == typeof(AgentFactoryRegistration))
            .Select(sd => sd.ImplementationInstance as AgentFactoryRegistration)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
}
