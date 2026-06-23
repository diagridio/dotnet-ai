// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Register_StoresToolByAgentAndFunctionName()
    {
        var registry = new ToolRegistry();
        var tool = AIFunctionFactory.Create(() => "ok", name: "probe");

        registry.Register("agent-a", tool);

        Assert.Same(tool, registry.Get("agent-a", "probe"));
    }

    [Fact]
    public void Get_AgentNameLookupIsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        var tool = AIFunctionFactory.Create(() => "ok", name: "probe");

        registry.Register("Agent-A", tool);

        Assert.Same(tool, registry.Get("agent-a", "probe"));
    }

    [Fact]
    public void Get_FunctionNameLookupIsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        var tool = AIFunctionFactory.Create(() => "ok", name: "Probe");

        registry.Register("agent-a", tool);

        Assert.Same(tool, registry.Get("agent-a", "probe"));
    }

    [Fact]
    public void Register_RejectsBlankAgentName()
    {
        var registry = new ToolRegistry();
        var tool = AIFunctionFactory.Create(() => "ok", name: "probe");

        Assert.Throws<ArgumentException>(() => registry.Register(" ", tool));
    }

    [Fact]
    public void Register_RejectsNullFunction()
    {
        var registry = new ToolRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("agent-a", null!));
    }

    [Fact]
    public void Get_MissingToolReturnsNull()
    {
        var registry = new ToolRegistry();

        Assert.Null(registry.Get("agent-a", "missing"));
    }
}
