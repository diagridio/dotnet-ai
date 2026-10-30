// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

/// <summary>
/// <see cref="WorkflowContextExtensions.CreateInvocation"/>, which every agent-run helper builds its workflow input with.
/// </summary>
public sealed class DaprAgentInvocationFactoryTests
{
    [Fact]
    public void CreateInvocation_OnlyTheRequiredValues_LeavesTheRestUnset()
    {
        var options = new AgentRunOptions();

        var invocation = WorkflowContextExtensions.CreateInvocation(new DaprAIAgent("alpha"), "hi", options);

        Assert.Equal(new DaprAgentInvocation("alpha", "hi", null, options), invocation);
    }

    [Fact]
    public void CreateInvocation_CarriesEachValue_AndCopiesTheBaggage()
    {
        var baggage = new Dictionary<string, string?> { ["tenant"] = "t1" };
        List<WorkflowChatMessage> prior = [new() { Role = "user", Content = "earlier" }];

        var invocation = WorkflowContextExtensions.CreateInvocation(new DaprAIAgent("alpha", "key"), "hi", null,
            conversationId: "c1", priorMessages: prior, telemetryBaggage: baggage);

        Assert.Equal("key", invocation.ChatClientKey);
        Assert.Equal("c1", invocation.ConversationId);
        Assert.Same(prior, invocation.PriorMessages);
        Assert.Equal(baggage, invocation.TelemetryBaggage);
        Assert.NotSame(baggage, invocation.TelemetryBaggage);
    }

    [Fact]
    public void CreateInvocation_EmptyBaggage_LeavesItUnset()
    {
        var invocation = WorkflowContextExtensions.CreateInvocation(new DaprAIAgent("alpha"), null, null,
            telemetryBaggage: new Dictionary<string, string?>());

        Assert.Null(invocation.TelemetryBaggage);
    }
}
