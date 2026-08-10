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

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Drives the Microsoft Agent Framework <see cref="AIContextProvider"/> pipeline for an agent.
/// <para>
/// This library bypasses MAF's agent-run pipeline (it unwraps the agent to its raw
/// <see cref="IChatClient"/> and re-runs the LLM/tool loop as durable Dapr Workflow activities),
/// so context providers — such as an <c>AgentSkillsProvider</c> — would never fire on their own.
/// This helper invokes them explicitly and collects the instructions, messages, and tools they
/// contribute for a turn, registering any invokable tool in the <see cref="ToolRegistry"/> so a
/// subsequent <see cref="ExecuteToolActivity"/> can resolve and execute it durably.
/// </para>
/// </summary>
internal static class ContextProviderPipeline
{
    // MAF wraps tools that require human approval in an ApprovalRequiredAIFunction. The durable
    // tool executor invokes AIFunctions directly and does not yet perform the approval round-trip,
    // so approval-gated tools (e.g. run_skill_script) are not exposed or registered until the
    // approval gate lands. Matched by type name to avoid coupling to experimental MEAI/MAF types.
    private const string ApprovalRequiredFunctionTypeName = "ApprovalRequiredAIFunction";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="tool"/> is an <see cref="AIFunction"/> that the
    /// durable runtime can invoke without a human-approval round-trip.
    /// </summary>
    public static bool TryGetInvokableFunction(AITool tool, out AIFunction function)
    {
        if (tool is AIFunction fn &&
            !string.Equals(tool.GetType().Name, ApprovalRequiredFunctionTypeName, StringComparison.Ordinal))
        {
            function = fn;
            return true;
        }

        function = null!;
        return false;
    }

    /// <summary>
    /// Invokes each context provider once, collecting its contributed instructions, messages, and
    /// invokable tools. Every invokable tool is registered in <paramref name="toolRegistry"/> under
    /// <paramref name="agentName"/>.
    /// </summary>
    public static async Task<Contribution> InvokeAsync(
        string agentName,
        AIAgent agent,
        IReadOnlyList<AIContextProvider> providers,
        ToolRegistry toolRegistry,
        CancellationToken cancellationToken)
    {
        var session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var instructions = new List<string>();
        var messages = new List<ChatMessage>();
        var tools = new List<AIFunction>();

        foreach (var provider in providers)
        {
#pragma warning disable MAAI001 // AIContextProvider context types are experimental (evaluation only).
            var invokingContext = new AIContextProvider.InvokingContext(agent, session, new AIContext());
#pragma warning restore MAAI001
            var aiContext = await provider.InvokingAsync(invokingContext, cancellationToken).ConfigureAwait(false);
            if (aiContext is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(aiContext.Instructions))
            {
                instructions.Add(aiContext.Instructions);
            }

            if (aiContext.Messages is not null)
            {
                messages.AddRange(aiContext.Messages);
            }

            if (aiContext.Tools is not null)
            {
                foreach (var tool in aiContext.Tools)
                {
                    if (TryGetInvokableFunction(tool, out var fn))
                    {
                        tools.Add(fn);
                        toolRegistry.Register(agentName, fn);
                    }
                }
            }
        }

        return new Contribution(agent, session, providers, instructions, messages, tools);
    }

    /// <summary>
    /// The instructions, messages, and tools contributed by an agent's context providers for a
    /// single LLM call, together with the agent and session used to invoke them.
    /// </summary>
    public sealed record Contribution(
        AIAgent Agent,
        AgentSession Session,
        IReadOnlyList<AIContextProvider> Providers,
        IReadOnlyList<string> Instructions,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<AIFunction> Tools);
}
