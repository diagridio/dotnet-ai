// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Catalyst;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Catalyst;

public sealed class CatalystMetadataTests
{
    [Fact]
    public void AgentMetadata_DefaultsDescribeDurableMicrosoftAgentFrameworkAgent()
    {
        var metadata = new AgentMetadata { AppId = "app-1", Type = "durable" };

        Assert.Equal("app-1", metadata.AppId);
        Assert.Equal("durable", metadata.Type);
        Assert.False(metadata.Orchestrator);
        Assert.Equal("Microsoft Agent Framework", metadata.Framework);
        Assert.Empty(metadata.Instructions);
        Assert.Empty(metadata.Metadata);
    }

    [Fact]
    public void AgentMetadata_UsesCatalystJsonPropertyNames()
    {
        var json = JsonSerializer.Serialize(new AgentMetadata
        {
            AppId = "app-1",
            Type = "durable",
            Role = "planner",
            Goal = "answer questions",
            Instructions = ["be concise"],
            SystemPrompt = "system",
            MaxIterations = 5,
            ToolChoice = "auto",
            Metadata = { ["team"] = "platform" }
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("app-1", root.GetProperty("appid").GetString());
        Assert.Equal("planner", root.GetProperty("role").GetString());
        Assert.Equal("answer questions", root.GetProperty("goal").GetString());
        Assert.Equal("be concise", root.GetProperty("instructions")[0].GetString());
        Assert.Equal("system", root.GetProperty("system_prompt").GetString());
        Assert.Equal(5, root.GetProperty("max_iterations").GetInt32());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.Equal("platform", root.GetProperty("metadata").GetProperty("team").GetString());
    }

    [Fact]
    public void AgentMetadataSchema_SerializesNestedMetadata()
    {
        var schema = new AgentMetadataSchema(
            "v1",
            new AgentMetadata { AppId = "app-1", Type = "durable", Instructions = ["stay focused"] },
            "agent-a")
        {
            Llm = new LlmMetadata("TestClient", "conversation.openai")
            {
                Api = "chat",
                Model = "model-a",
                ResourceName = "llm-resource",
                BaseUrl = "https://example.test",
                AzureEndpoint = "https://azure.example.test",
                AzureDeployment = "deployment-a",
                PromptTemplate = "template-a"
            },
            Registry = new RegistryMetadata { ResourceName = "state-store", Name = "team-a" },
            Tools = [new ToolMetadata("lookup", "looks up data", "{}")]
        };

        var json = JsonSerializer.Serialize(schema);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("v1", root.GetProperty("version").GetString());
        Assert.Equal("agent-a", root.GetProperty("name").GetString());
        Assert.Equal("app-1", root.GetProperty("agent").GetProperty("appid").GetString());
        Assert.Equal("TestClient", root.GetProperty("llm").GetProperty("client").GetString());
        Assert.Equal("conversation.openai", root.GetProperty("llm").GetProperty("provider").GetString());
        Assert.Equal("llm-resource", root.GetProperty("llm").GetProperty("resource_name").GetString());
        Assert.Equal("state-store", root.GetProperty("registry").GetProperty("resource_name").GetString());
        Assert.Equal("lookup", root.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void RegisteredAgentList_UsesAgentsPropertyName()
    {
        var json = JsonSerializer.Serialize(new RegisteredAgentList
        {
            AgentNames = ["agent-a", "agent-b"]
        });

        using var document = JsonDocument.Parse(json);

        Assert.Equal("agent-a", document.RootElement.GetProperty("agents")[0].GetString());
        Assert.Equal("agent-b", document.RootElement.GetProperty("agents")[1].GetString());
    }

    [Fact]
    public void DiagridCatalystOptions_DefaultsToLatestSchemaAndEmptyRegistry()
    {
        var options = new DiagridCatalystOptions();

        Assert.Equal("latest", options.SchemaVersion);
        Assert.NotNull(options.Registry);
        Assert.Null(options.Registry.ResourceName);
        Assert.Null(options.Registry.Name);
    }
}
