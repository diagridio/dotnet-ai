// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow.Payloads;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

public sealed class DaprAgentsBuilderExtensionsBinaryStoreTests
{
    [Fact]
    public void WithBinaryStorePayloads_SetsStoreNameAndPreferReference()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithBinaryStorePayloads("agent-payloads");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BinaryStorePayloadOptions>>().Value;
        Assert.Equal("agent-payloads", options.StoreName);
        Assert.Equal(PayloadWritePreference.PreferReference, options.WritePreference);
        Assert.Equal(64 * 1024, options.OffloadThreshold);
    }

    [Fact]
    public void WithBinaryStorePayloads_ConfigureRunsAfterDefaults()
    {
        var services = new ServiceCollection();

        services.AddDaprAgents().WithBinaryStorePayloads("agent-payloads", options =>
        {
            options.OffloadThreshold = null;
            options.KeyPrefix = "agents";
            options.WritePreference = PayloadWritePreference.Preserve;
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BinaryStorePayloadOptions>>().Value;
        Assert.Null(options.OffloadThreshold);
        Assert.Equal("agents", options.KeyPrefix);
        Assert.Equal(PayloadWritePreference.Preserve, options.WritePreference);
    }

    [Fact]
    public void WithBinaryStorePayloads_NullBuilder_Throws()
    {
        IAgentsBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.WithBinaryStorePayloads("store"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithBinaryStorePayloads_WhitespaceStoreName_Throws(string storeName)
    {
        var builder = new ServiceCollection().AddDaprAgents();

        Assert.Throws<ArgumentException>(() => builder.WithBinaryStorePayloads(storeName));
    }

    [Fact]
    public void WithBinaryStorePayloads_BuilderWithoutWorkflowBuilder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new FakeBuilder().WithBinaryStorePayloads("store"));
    }

    private sealed class FakeBuilder : IAgentsBuilder
    {
        public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory) => this;
        public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory) => this;
        public IAgentsBuilder WithCatalyst(DiagridCatalystOptions options) => this;
        public IAgentsBuilder WithCatalyst() => this;
    }
}
