// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Dapr.Metadata.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Catalyst;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Catalyst;

/// <summary>
/// Checks the registry documents against dapr-agents' agent-metadata JSON schema, the format the
/// Catalyst Agents view reads. The schema file is vendored from dapr/dapr-agents
/// (schemas/agent-metadata/v1.0.0.json); refresh it when dapr-agents publishes a new version.
/// </summary>
public sealed class CatalystMetadataSchemaConformanceTests
{
    private const string AgentName = "agent-a";

    // DaprStateManagementClient writes state with JsonSerializerDefaults.Web (Dapr.Common
    // DaprGenericClientBuilder), so validate exactly what the registry stores.
    private static readonly JsonSerializerOptions StateSerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BuildAgentMetadata_WithToolsAndComponent_ConformsToSchema()
    {
        var lookup = AIFunctionFactory.Create((string city) => city, "lookup", "Looks up a city.");
        var service = CreateService(chatClientRegistry =>
            chatClientRegistry.Register(AgentName, Mock.Of<IChatClient>(), "Be helpful.", [lookup]));

        var errors = Validate(service.BuildAgentMetadata(CreateAgent(), CreateDaprMetadata()));

        Assert.Empty(errors);
    }

    [Fact]
    public void BuildAgentMetadata_WithoutChatClientConfig_ConformsToSchema()
    {
        var service = CreateService(_ => { });

        var errors = Validate(service.BuildAgentMetadata(CreateAgent(), new DaprMetadata()));

        Assert.Empty(errors);
    }

    [Fact]
    public void BuildAgentMetadata_PublishesToolArgumentSchemaAsArgs()
    {
        var lookup = AIFunctionFactory.Create((string city) => city, "lookup", "Looks up a city.");
        var service = CreateService(chatClientRegistry =>
            chatClientRegistry.Register(AgentName, Mock.Of<IChatClient>(), null, [lookup]));

        var json = JsonSerializer.SerializeToElement(
            service.BuildAgentMetadata(CreateAgent(), CreateDaprMetadata()), StateSerializerOptions);

        var tool = json.GetProperty("tools")[0];
        Assert.False(tool.TryGetProperty("arguments", out _));
        using var args = JsonDocument.Parse(tool.GetProperty("args").GetString()!);
        Assert.True(args.RootElement.GetProperty("properties").TryGetProperty("city", out _));
    }

    [Fact]
    public void Validator_RejectsTheFormerArgumentsPropertyName()
    {
        using var document = JsonDocument.Parse(
            """{"version":"1.0.0","name":"a","registered_at":"now","agent":{"appid":"app","type":"durable"},"tools":[{"name":"t","description":"d","arguments":"{}"}]}""");

        var errors = JsonSchemaSubsetValidator.Validate(document.RootElement, LoadSchema());

        Assert.Contains(errors, e => e.Contains("missing required property 'args'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("$.tools[0].arguments", StringComparison.Ordinal));
    }

    private static List<string> Validate(AgentMetadataSchema metadata) =>
        JsonSchemaSubsetValidator.Validate(
            JsonSerializer.SerializeToElement(metadata, StateSerializerOptions),
            LoadSchema());

    private static JsonElement LoadSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalyst", "Schemas", "agent-metadata-v1.0.0.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static AIAgent CreateAgent() =>
        new ChatClientAgent(Mock.Of<IChatClient>(), instructions: null, name: AgentName);

    private static DaprMetadata CreateDaprMetadata() => new()
    {
        AppId = "app-1",
        RuntimeVersion = "1.19.0",
        Components = [new ComponentMetadata { Name = "conversation-ollama", Type = "conversation.openai", Version = "v1" }],
    };

    private static CatalystAgentRegistryHostedService CreateService(Action<ChatClientRegistry> configure)
    {
        var chatClientRegistry = new ChatClientRegistry();
        configure(chatClientRegistry);

        // BuildAgentMetadata reads only the chat client registry and the options; the state
        // client and the agent registry are used by StartAsync.
        return new CatalystAgentRegistryHostedService(
            agentRegistry: null!,
            chatClientRegistry,
            stateClient: null!,
            serviceProvider: null!,
            Options.Create(new DaprMetadata()),
            Options.Create(new DiagridCatalystOptions()));
    }
}
