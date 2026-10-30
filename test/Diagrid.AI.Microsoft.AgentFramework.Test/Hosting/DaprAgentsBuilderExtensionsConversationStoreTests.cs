// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderExtensionsConversationStoreTests
{
    [Fact]
    public void WithConversationStore_RegistersTheDaprStore()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithConversationStore("statestore");

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IConversationStore>();
        Assert.IsType<DaprConversationStore>(store);
        Assert.Same(store, provider.GetRequiredService<IConversationStore>());
    }

    [Fact]
    public void WithConversationStore_AppliesTheSettings()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithConversationStore("statestore", options => options.KeyPrefix = "p:");

        var store = (DaprConversationStore)services.BuildServiceProvider().GetRequiredService<IConversationStore>();
        Assert.Equal("p:abc", store.ManifestKey("abc"));
    }

    [Fact]
    public void WithConversationStore_KeepsAStoreTheHostRegisteredFirst()
    {
        var services = new ServiceCollection();
        var custom = Mock.Of<IConversationStore>();
        services.AddSingleton(custom);

        services.AddDaprAgents().WithConversationStore("statestore");

        Assert.Same(custom, services.BuildServiceProvider().GetRequiredService<IConversationStore>());
    }

    [Fact]
    public void WithConversationStore_InvalidSettings_ThrowAtRegistration()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithConversationStore("statestore", options => options.TurnDedupeWindow = 1));
        Assert.Throws<ArgumentException>(() => builder.WithConversationStore("statestore", options => options.StateStoreName = ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithConversationStore_WhitespaceStoreName_Throws(string stateStoreName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.ThrowsAny<ArgumentException>(() => builder.WithConversationStore(stateStoreName));
    }

    [Fact]
    public void WithConversationStore_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithConversationStore("statestore"));
    }

    [Fact]
    public void WithoutConversationStore_NothingIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents();

        Assert.Null(services.BuildServiceProvider().GetService<IConversationStore>());
    }
}
