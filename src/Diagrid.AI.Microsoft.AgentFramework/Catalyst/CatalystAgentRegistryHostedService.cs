// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Dapr.Metadata.Abstractions;
using Dapr.StateManagement;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Microsoft.AgentFramework.Catalyst;

internal sealed class CatalystAgentRegistryHostedService(
	AgentRegistry agentRegistry,
	ChatClientRegistry chatClientRegistry,
	DaprStateManagementClient stateClient,
	IServiceProvider serviceProvider,
	IOptions<DaprMetadata> daprMetadataProvider,
	IOptions<DiagridCatalystOptions> options) : IHostedService
{
	private const string RegisteredAgentsStateKey = "agents:default:_index";
	private const string AgentMetadataStateKeyPrefix = "agents:default";
	private readonly DaprMetadata _daprMetadata = daprMetadataProvider.Value;
	private readonly DiagridCatalystOptions _options = options.Value;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(_options.Registry.ResourceName))
			throw new InvalidOperationException(
				"Diagrid Catalyst requires DiagridCatalystOptions.Registry.ResourceName.");

		var agents = agentRegistry.MaterializeAll(serviceProvider);
		var agentNames = agents
			.Select(agent => agent.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await stateClient.SaveStateAsync(
				_options.Registry.ResourceName,
				RegisteredAgentsStateKey,
				new RegisteredAgentList { AgentNames = agentNames },
				cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		foreach (var agent in agents)
		{
			if (string.IsNullOrWhiteSpace(agent.Name))
				continue;

			var schema = BuildAgentMetadata(agent, _daprMetadata);
			await stateClient.SaveStateAsync(
					_options.Registry.ResourceName,
					GetAgentStateKey(agent.Name),
					schema,
					cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private static string GetAgentStateKey(string agentName) => $"{AgentMetadataStateKeyPrefix}:{agentName}";

	private AgentMetadataSchema BuildAgentMetadata(AIAgent agent, DaprMetadata daprMetadata)
	{
		var config = chatClientRegistry.Get(agent.Name!);
		var conversationComponent = FindConversationComponent(daprMetadata, agent.Name!, config?.ChatClient);

		return new AgentMetadataSchema(
			_options.SchemaVersion,
			new AgentMetadata
			{
				AppId = daprMetadata.AppId ?? "unknown",
				Type = "durable",
				SystemPrompt = config?.Instructions,
				Instructions = SplitInstructions(config?.Instructions),
				Framework = "Microsoft Agent Framework",
				Metadata =
				{
					["dapr.runtime_version"] = daprMetadata.RuntimeVersion ?? "unknown",
				},
			},
			agent.Name!)
		{
			Llm = BuildLlmMetadata(config, conversationComponent),
			Registry = _options.Registry,
			Tools = BuildToolMetadata(config?.Tools),
		};
	}

	private static LlmMetadata? BuildLlmMetadata(ChatClientRegistry.AgentChatConfig? config, ComponentMetadata? component)
	{
		if (config is null)
			return null;

		var clientName = config.ChatClient.GetType().Name;
		var provider = component?.Type ?? "unknown";
		return new LlmMetadata(clientName, provider)
		{
			ResourceName = component?.Name,
		};
	}

	private static ComponentMetadata? FindConversationComponent(
		DaprMetadata metadata,
		string agentName,
		IChatClient? chatClient)
	{
		var configComponent = GetComponents(metadata).FirstOrDefault(component =>
			component.Name?.Equals(agentName, StringComparison.OrdinalIgnoreCase) == true &&
			component.Type?.StartsWith("conversation.", StringComparison.OrdinalIgnoreCase) == true);

		if (configComponent is not null)
			return configComponent;

		return chatClient is null
			? null
			: GetComponents(metadata).FirstOrDefault(component =>
				component.Type?.StartsWith("conversation.", StringComparison.OrdinalIgnoreCase) == true);
	}

	private static IEnumerable<ComponentMetadata> GetComponents(DaprMetadata metadata) =>
		metadata.Components;

	private static List<string> SplitInstructions(string? instructions) =>
		string.IsNullOrWhiteSpace(instructions)
			? []
			: [instructions];

	private static List<ToolMetadata> BuildToolMetadata(IList<AITool>? tools)
	{
		if (tools is null || tools.Count == 0)
			return [];

		return tools
			.Select(tool => new ToolMetadata(
				tool.Name,
				tool.Description,
				"{}"))
			.ToList();
	}
}
