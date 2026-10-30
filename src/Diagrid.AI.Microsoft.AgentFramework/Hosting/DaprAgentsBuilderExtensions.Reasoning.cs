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

using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

public static partial class DaprAgentsBuilderExtensions
{
    // =========================================================================
    // Model reasoning
    //
    // ChatOptions.Reasoning asks a reasoning model for an effort level. CallLlmActivity builds the
    // ChatOptions for every LLM call itself, so the effort is registered here and resolved per call,
    // like the LLM timeout. Reasoning the model returns is kept and sent back to it (see
    // WorkflowChatMessage.Reasoning); WithReasoningReplay limits that where it costs too much.
    // =========================================================================

    /// <summary>
    /// Sets the reasoning effort requested on every LLM call of any agent that has no effort of its own.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="effort">The reasoning effort, sent as <see cref="ChatOptions.Reasoning"/>.</param>
    /// <param name="output">The reasoning output to request, or <see langword="null"/> for the provider's default.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// An agent's own <see cref="ChatOptions.Reasoning"/>, and <c>WithReasoning(agentName, ...)</c>,
    /// take precedence. Chat clients that do not support reasoning options ignore it; the Dapr
    /// Conversation API has no field for it today.
    /// </remarks>
    public static IAgentsBuilder WithReasoning(
        this IAgentsBuilder builder,
        ReasoningEffort effort,
        ReasoningOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ReasoningEffortRegistration.Validate(effort, output);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ReasoningEffortRegistration(null, effort, output));

        return builder;
    }

    /// <summary>
    /// Sets the reasoning effort requested on every LLM call of the named agent, overriding the
    /// agent's own <see cref="ChatOptions.Reasoning"/> and the default set by
    /// <see cref="WithReasoning(IAgentsBuilder, ReasoningEffort, ReasoningOutput?)"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent the effort applies to.</param>
    /// <param name="effort">The reasoning effort, sent as <see cref="ChatOptions.Reasoning"/>.</param>
    /// <param name="output">The reasoning output to request, or <see langword="null"/> for the provider's default.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// Can be called before or after <c>WithAgent(...)</c> for the same agent name.
    /// </remarks>
    public static IAgentsBuilder WithReasoning(
        this IAgentsBuilder builder,
        string agentName,
        ReasoningEffort effort,
        ReasoningOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ReasoningEffortRegistration.Validate(effort, output);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ReasoningEffortRegistration(agentName, effort, output));

        return builder;
    }

    /// <summary>
    /// Sets how much earlier reasoning is sent back to the model, for every agent that has no setting
    /// of its own. Defaults to <see cref="ReasoningReplay.AcrossTurns"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="replay">How much reasoning to send back and keep.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// Sending reasoning back costs input tokens on every call, and keeping it makes the run's
    /// messages, and the workflow payloads that carry them, larger.
    /// <see cref="ReasoningReplay.WithinRun"/> keeps it out of the history passed between turns;
    /// <see cref="ReasoningReplay.Off"/> keeps it out of the messages altogether.
    /// </remarks>
    public static IAgentsBuilder WithReasoningReplay(this IAgentsBuilder builder, ReasoningReplay replay)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ReasoningReplayRegistration.Validate(replay);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ReasoningReplayRegistration(null, replay));

        return builder;
    }

    /// <summary>
    /// Sets how much earlier reasoning is sent back to the model for the named agent, overriding the
    /// default set by <see cref="WithReasoningReplay(IAgentsBuilder, ReasoningReplay)"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent the setting applies to.</param>
    /// <param name="replay">How much reasoning to send back and keep.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// Can be called before or after <c>WithAgent(...)</c> for the same agent name.
    /// </remarks>
    public static IAgentsBuilder WithReasoningReplay(this IAgentsBuilder builder, string agentName, ReasoningReplay replay)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ReasoningReplayRegistration.Validate(replay);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ReasoningReplayRegistration(agentName, replay));

        return builder;
    }
}
