// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

// AddDaprConversationClient is [Experimental("DAPR_CONVERSATION")] in the Dapr SDK.
#pragma warning disable DAPR_CONVERSATION

using Dapr.AI.Conversation;
using Dapr.AI.Conversation.Extensions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderExtensionsLlmTimeoutTests
{
    [Fact]
    public void WithLlmTimeout_Default_RegistersUnnamedTimeout()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithLlmTimeout(TimeSpan.FromMinutes(5));

        var registration = Assert.Single(services.BuildServiceProvider().GetServices<LlmTimeoutRegistration>());
        Assert.Null(registration.AgentName);
        Assert.Equal(TimeSpan.FromMinutes(5), registration.Timeout);
    }

    [Fact]
    public void WithLlmTimeout_Named_RegistersTimeoutForAgent()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithLlmTimeout("slow-agent", Timeout.InfiniteTimeSpan);

        var registration = Assert.Single(services.BuildServiceProvider().GetServices<LlmTimeoutRegistration>());
        Assert.Equal("slow-agent", registration.AgentName);
        Assert.Equal(Timeout.InfiniteTimeSpan, registration.Timeout);
    }

    [Fact]
    public void WithLlmTimeout_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithLlmTimeout(TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentNullException>(() => builder.WithLlmTimeout("agent", TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithLlmTimeout_WhitespaceAgentName_Throws(string agentName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithLlmTimeout(agentName, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void WithLlmTimeout_NonPositive_Throws(int seconds)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithLlmTimeout(TimeSpan.FromSeconds(seconds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithLlmTimeout("agent", TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void WithLlmTimeout_AboveMaximum_Throws()
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithLlmTimeout(LlmTimeoutRegistration.MaxTimeout + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void AddDaprAgents_RegistersConversationClientWhenAbsent()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents();

        Assert.Single(services, d => d.ServiceType == typeof(DaprConversationClient));
    }

    [Fact]
    public void AddDaprAgents_KeepsConversationClientRegisteredByHost()
    {
        var services = new ServiceCollection();
        var configured = false;
        services.AddDaprConversationClient((_, clientBuilder) =>
        {
            configured = true;
            clientBuilder.UseTimeout(TimeSpan.FromMinutes(5));
        });

        services.AddDaprAgents();

        Assert.Single(services, d => d.ServiceType == typeof(DaprConversationClient));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<DaprConversationClient>();
        Assert.True(configured);
    }

    [Fact]
    public void AddDaprAgents_RegistersTheUnkeyedConversationClient_WhenTheHostRegisteredOnlyAKeyedOne()
    {
        // DaprChatClient resolves the unkeyed client, so a keyed one cannot satisfy it.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DaprConversationClient>("host-key", (_, _) => null!);

        services.AddDaprAgents();

        Assert.Single(services, d => d.ServiceType == typeof(DaprConversationClient) && !d.IsKeyedService);
        Assert.Single(services, d => d.ServiceType == typeof(DaprConversationClient) && d.IsKeyedService);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<DaprConversationClient>());
    }
}
