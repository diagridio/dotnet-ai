// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class LlmTimeoutRegistrationTests
{
    [Fact]
    public void Resolve_NothingRegistered_ReturnsDefault()
    {
        Assert.Equal(LlmTimeoutRegistration.DefaultTimeout, LlmTimeoutRegistration.Resolve([], "agent"));
    }

    [Fact]
    public void Resolve_OnlyDefaultRegistered_ReturnsIt()
    {
        var registrations = new[] { new LlmTimeoutRegistration(null, TimeSpan.FromMinutes(3)) };

        Assert.Equal(TimeSpan.FromMinutes(3), LlmTimeoutRegistration.Resolve(registrations, "agent"));
    }

    [Fact]
    public void Resolve_NamedRegistration_WinsOverDefault_RegardlessOfOrder()
    {
        var registrations = new[]
        {
            new LlmTimeoutRegistration("agent", TimeSpan.FromMinutes(10)),
            new LlmTimeoutRegistration(null, TimeSpan.FromMinutes(3)),
        };

        Assert.Equal(TimeSpan.FromMinutes(10), LlmTimeoutRegistration.Resolve(registrations, "agent"));
        Assert.Equal(TimeSpan.FromMinutes(3), LlmTimeoutRegistration.Resolve(registrations, "other"));
    }

    [Fact]
    public void Resolve_RepeatedRegistrations_LastWins()
    {
        var registrations = new[]
        {
            new LlmTimeoutRegistration("agent", TimeSpan.FromMinutes(1)),
            new LlmTimeoutRegistration("agent", TimeSpan.FromMinutes(2)),
        };

        Assert.Equal(TimeSpan.FromMinutes(2), LlmTimeoutRegistration.Resolve(registrations, "agent"));
    }

    [Fact]
    public void Resolve_AgentNameIsCaseInsensitive_LikeTheAgentRegistry()
    {
        var registrations = new[] { new LlmTimeoutRegistration("Agent", TimeSpan.FromMinutes(10)) };

        Assert.Equal(TimeSpan.FromMinutes(10), LlmTimeoutRegistration.Resolve(registrations, "agent"));
    }
}
