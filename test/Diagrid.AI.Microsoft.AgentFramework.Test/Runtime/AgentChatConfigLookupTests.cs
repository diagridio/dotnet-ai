// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class AgentChatConfigLookupTests
{
    [Fact]
    public void Find_Registered_DoesNotRunTheFactory()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var chatClients = new ChatClientRegistry();
        chatClients.Register("alpha", new CallLlmActivityTests.CapturingChatClient(), "be brief", tools: null);
        var agents = new AgentRegistry(provider, []);
        var calls = 0;
        agents.AddFactory(_ => { calls++; return new TestAIAgent("alpha"); }, chatClientKey: null, agentName: "alpha", provider);

        var config = AgentChatConfigLookup.Find(chatClients, agents, provider, "alpha", null);

        Assert.Equal("be brief", config!.Instructions);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Find_NotYetRegistered_RunsTheFactoryWhichRegistersIt()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var chatClients = new ChatClientRegistry();
        var agents = new AgentRegistry(provider, []);
        agents.AddFactory(_ =>
        {
            chatClients.Register("alpha", new CallLlmActivityTests.CapturingChatClient(), "lazy", tools: null);
            return new TestAIAgent("alpha");
        }, chatClientKey: null, agentName: "alpha", provider);

        var config = AgentChatConfigLookup.Find(chatClients, agents, provider, "alpha", null);

        Assert.Equal("lazy", config!.Instructions);
    }

    [Fact]
    public void Find_AgentRegistersNoChatClient_ReturnsNull()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var agents = new AgentRegistry(provider, []);
        agents.AddFactory(_ => new TestAIAgent("alpha"), chatClientKey: null, agentName: "alpha", provider);

        Assert.Null(AgentChatConfigLookup.Find(new ChatClientRegistry(), agents, provider, "alpha", null));
    }
}
