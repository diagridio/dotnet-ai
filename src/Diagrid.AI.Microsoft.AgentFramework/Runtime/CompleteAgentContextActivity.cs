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

using System.Text.Json;
using Dapr.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Activity that runs the <c>AIContextProvider.InvokedAsync</c> pipeline once per agent run, after a
/// final response (or a failure) is produced — the counterpart to <see cref="ResolveAgentContextActivity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ResolveAgentContextActivity"/> (whose failures fail the run, since its
/// contribution materially affects the LLM calls that follow), a provider's <c>InvokedAsync</c> is a
/// best-effort notification about work that already finished: failures are logged and swallowed so a
/// provider's bookkeeping bug can never turn an already-successful (or already-failed) run into a
/// different failure.
/// </para>
/// <para>
/// Note (confirmed empirically): MAF's own <c>AIContextProvider.InvokedAsync</c> template skips
/// calling the provider's overridable <c>StoreAIContextAsync</c> hook when
/// <c>InvokedContext.InvokeException</c> is set — a provider only observes failures if it overrides
/// the lower-level <c>InvokedCoreAsync</c> instead. We still call <c>InvokedAsync</c> uniformly for
/// both outcomes; whether a given provider reacts to failures is up to how it's implemented.
/// </para>
/// <para>
/// Reconstructs the SAME logical <c>AgentSession</c> <see cref="ResolveAgentContextActivity"/> used
/// (via <see cref="CompleteAgentContextInput.SerializedSessionJson"/>), and establishes
/// <c>AIAgent.CurrentRunContext</c> — including <see cref="CompleteAgentContextInput.Options"/> — for
/// the duration of the provider loop via <see cref="AgentRunContextScope"/>, so a provider sees the
/// same invocation metadata during <c>InvokedAsync</c> that it saw during <c>InvokingAsync</c>.
/// </para>
/// </remarks>
internal sealed partial class CompleteAgentContextActivity(
    ChatClientRegistry chatClientRegistry,
    AgentRegistry agentRegistry,
    IServiceProvider serviceProvider,
    ILogger<CompleteAgentContextActivity> logger) : WorkflowActivity<CompleteAgentContextInput, CompleteAgentContextOutput>
{
    /// <inheritdoc />
    public override async Task<CompleteAgentContextOutput> RunAsync(WorkflowActivityContext context, CompleteAgentContextInput input)
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
            return new CompleteAgentContextOutput();
        }

        var agent = agentRegistry.Get(input.AgentName, input.ChatClientKey, serviceProvider);
        var session = await GetSessionAsync(agent, input.SerializedSessionJson).ConfigureAwait(false);
        var requestMessages = input.RequestMessages.Select(WorkflowChatMessageConverter.ToChatMessage).ToList();

        using (AgentRunContextScope.Enter(agent, session, requestMessages, input.Options))
        {
            foreach (var provider in providers)
            {
                try
                {
                    // AIContextProvider.InvokedContext's constructor is [Experimental("MAAI001")] even
                    // though AIContextProvider/InvokedAsync itself is not — MAF is still iterating on its
                    // exact shape. This type is internal plumbing, not part of our public API surface.
#pragma warning disable MAAI001
                    var invokedContext = input.ErrorMessage is not null
                        ? new AIContextProvider.InvokedContext(agent, session, requestMessages, new InvalidOperationException(input.ErrorMessage))
                        : new AIContextProvider.InvokedContext(
                            agent,
                            session,
                            requestMessages,
                            input.ResponseMessages?.Select(WorkflowChatMessageConverter.ToChatMessage) ?? []);
#pragma warning restore MAAI001

                    await provider.InvokedAsync(invokedContext, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogProviderError(input.AgentName, provider.GetType().Name, ex.Message);
                }
            }
        }

        return new CompleteAgentContextOutput();
    }

    /// <summary>
    /// Reconstructs the same logical session <see cref="ResolveAgentContextActivity"/> used, from its
    /// serialized state, so provider state written during <c>InvokingAsync</c> (e.g. to
    /// <c>Session.StateBag</c>) is visible here. Falls back to a fresh session when none was
    /// serialized (e.g. this agent's providers were only just registered and never resolved).
    /// </summary>
    private static async Task<AgentSession> GetSessionAsync(AIAgent agent, string? serializedSessionJson)
    {
        if (string.IsNullOrEmpty(serializedSessionJson))
        {
            return await agent.CreateSessionAsync().ConfigureAwait(false);
        }

        var serialized = JsonSerializer.Deserialize<JsonElement>(serializedSessionJson);
        return await agent.DeserializeSessionAsync(serialized).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Warning, "Context provider '{ProviderType}' failed InvokedAsync for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogProviderError(string agentName, string providerType, string errorMessage);
}
