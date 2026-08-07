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
        var session = await agent.CreateSessionAsync().ConfigureAwait(false);
        var requestMessages = input.RequestMessages.Select(WorkflowChatMessageConverter.ToChatMessage).ToList();

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

        return new CompleteAgentContextOutput();
    }

    [LoggerMessage(LogLevel.Warning, "Context provider '{ProviderType}' failed InvokedAsync for agent '{AgentName}': {ErrorMessage}")]
    private partial void LogProviderError(string agentName, string providerType, string errorMessage);
}
