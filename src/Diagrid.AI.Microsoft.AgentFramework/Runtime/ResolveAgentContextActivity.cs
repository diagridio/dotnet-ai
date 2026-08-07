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

using Dapr.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that runs the <c>AIContextProvider.InvokingAsync</c> pipeline once per agent run — the
/// durable-workflow equivalent of what <c>ChatClientAgent.RunAsync</c> does internally before handing
/// off to <c>FunctionInvokingChatClient</c>. Resolves the <c>AIContextProvider</c> instances configured
/// for the agent (e.g. an MAF <c>AgentSkillsProvider</c>, via <see cref="ChatClientRegistry"/>) and
/// threads a single accumulating <c>AIContext</c> through all of them in order, producing a
/// serializable contribution that <see cref="AgentRunWorkflow"/> threads through every
/// <see cref="CallLlmActivity"/> call for the rest of the run.
/// </summary>
/// <remarks>
/// <para>
/// Providers are chained rather than invoked independently: <c>AIContextProvider.InvokingAsync</c>'s
/// own template implementation merges whatever is already in the <c>AIContext</c> passed to it with
/// its contribution (confirmed empirically — a provider that doesn't touch <c>Instructions</c>/
/// <c>Messages</c>/<c>Tools</c> echoes the input back unchanged), so feeding provider N's output as
/// provider N+1's input is what accumulates contributions correctly, exactly like
/// <c>AgentSkillsProviderBuilder</c>'s own guidance for mixing multiple sources.
/// </para>
/// <para>
/// The seed <c>AIContext</c> is intentionally empty (no <c>Messages</c>): since the echo behavior
/// above would otherwise reflect the seed straight into the final contribution, seeding it with the
/// run's new user message would make that message reappear as an "ephemeral" addition and get
/// duplicated once <see cref="CallLlmActivity"/> also sends the real conversation history.
/// </para>
/// <para>
/// Any <see cref="AIFunction"/> tools contributed by a provider (e.g. a skills provider's
/// <c>load_skill</c>/<c>read_skill_resource</c>/<c>run_skill_script</c>) are registered into
/// <see cref="ToolRegistry"/> under the agent's name, so <see cref="ExecuteToolActivity"/> can resolve
/// and invoke them exactly like any other tool — no changes to tool execution were needed.
/// </para>
/// </remarks>
internal sealed partial class ResolveAgentContextActivity(
    ChatClientRegistry chatClientRegistry,
    ToolRegistry toolRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<ResolveAgentContextActivity> logger) : WorkflowActivity<ResolveAgentContextInput, ResolveAgentContextOutput>
{
    /// <inheritdoc />
    public override async Task<ResolveAgentContextOutput> RunAsync(WorkflowActivityContext context, ResolveAgentContextInput input)
    {
        var config = chatClientRegistry.Get(input.AgentName);
        if (config is null)
        {
            // Trigger lazy agent resolution — this runs the factory which populates
            // ChatClientRegistry (including any configured AIContextProviders) as a side effect.
            agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
            config = chatClientRegistry.Get(input.AgentName);
        }

        if (config?.ContextProviders is not { Count: > 0 } providers)
        {
            return new ResolveAgentContextOutput();
        }

        LogResolvingContext(input.AgentName, providers.Count);

        var agent = agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
        var session = await agent.CreateSessionAsync().ConfigureAwait(false);

        var accumulated = new AIContext();
        foreach (var provider in providers)
        {
            try
            {
                // AIContextProvider.InvokingContext's constructor is [Experimental("MAAI001")] even
                // though AIContextProvider/InvokingAsync itself is not — MAF is still iterating on
                // its exact shape. This type is internal plumbing, not part of our public API surface.
#pragma warning disable MAAI001
                var invokingContext = new AIContextProvider.InvokingContext(agent, session, accumulated);
#pragma warning restore MAAI001

                accumulated = await provider.InvokingAsync(invokingContext, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogProviderError(input.AgentName, provider.GetType().Name, ex.Message);
                throw;
            }
        }

        List<string>? toolNames = null;
        if (accumulated.Tools is not null)
        {
            foreach (var tool in accumulated.Tools)
            {
                if (tool is not AIFunction function)
                {
                    continue;
                }

                toolRegistry.Register(input.AgentName, function);
                (toolNames ??= []).Add(function.Name);
            }
        }

        return new ResolveAgentContextOutput
        {
            Instructions = string.IsNullOrWhiteSpace(accumulated.Instructions) ? null : accumulated.Instructions,
            Messages = accumulated.Messages?.Select(WorkflowChatMessageConverter.FromChatMessage).ToList(),
            ToolNames = toolNames
        };
    }

    [LoggerMessage(LogLevel.Debug, "Resolving agent context for '{AgentName}' ({ProviderCount} context provider(s))")]
    private partial void LogResolvingContext(string agentName, int providerCount);

    [LoggerMessage(LogLevel.Error, "Context provider '{ProviderType}' failed InvokingAsync for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogProviderError(string agentName, string providerType, string errorMessage);
}
