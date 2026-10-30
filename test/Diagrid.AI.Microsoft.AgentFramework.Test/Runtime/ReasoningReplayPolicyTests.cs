// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ReasoningReplayPolicyTests
{
    [Theory]
    [InlineData(ReasoningReplay.AcrossTurns, 0, true)]
    [InlineData(ReasoningReplay.AcrossTurns, 2, true)]
    [InlineData(ReasoningReplay.WithinRun, 0, false)]
    [InlineData(ReasoningReplay.WithinRun, 1, false)]
    [InlineData(ReasoningReplay.WithinRun, 2, true)]
    [InlineData(ReasoningReplay.WithinRun, 3, true)]
    [InlineData(ReasoningReplay.Off, 0, false)]
    [InlineData(ReasoningReplay.Off, 3, false)]
    public void SendsReasoning_FollowsTheSetting_WithTheRunStartingAtIndexTwo(ReasoningReplay replay, int index, bool expected)
    {
        Assert.Equal(expected, ReasoningReplayPolicy.SendsReasoning(replay, index, runMessagesStart: 2));
    }

    [Fact]
    public void SendsReasoning_AnUnknownSetting_SendsLikeAcrossTurns()
    {
        Assert.True(ReasoningReplayPolicy.SendsReasoning((ReasoningReplay)42, 0, runMessagesStart: 5));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(ReasoningReplay.AcrossTurns, true)]
    [InlineData(ReasoningReplay.WithinRun, false)]
    [InlineData(ReasoningReplay.Off, false)]
    public void KeepsReasoningAcrossTurns_OnlyWhenEarlierTurnsSendItBack(ReasoningReplay? replay, bool expected)
    {
        Assert.Equal(expected, ReasoningReplayPolicy.KeepsReasoningAcrossTurns(replay));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(ReasoningReplay.AcrossTurns, true)]
    [InlineData(ReasoningReplay.WithinRun, true)]
    [InlineData(ReasoningReplay.Off, false)]
    public void KeepsReasoningWithinRun_UnlessOff(ReasoningReplay? replay, bool expected)
    {
        Assert.Equal(expected, ReasoningReplayPolicy.KeepsReasoningWithinRun(replay));
    }
}
