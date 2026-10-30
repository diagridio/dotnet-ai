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

using Dapr.StateManagement;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

public static partial class DaprAgentsBuilderExtensions
{
    // =========================================================================
    // Conversation store
    //
    // History kept in a Dapr state store instead of workflow payloads, so a run started with a
    // conversation id continues from the earlier turns, model reasoning included, without carrying
    // them in every activity input. Port of dapr-agents DaprSessionStore.
    // =========================================================================

    /// <summary>
    /// Keeps agent conversation history, including model reasoning, in a Dapr state store, so runs started
    /// with a conversation id continue from the earlier turns without carrying them in workflow payloads.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="stateStoreName">
    /// The Dapr state store component. It must support ETags, and also TTL when
    /// <see cref="DaprConversationStoreOptions.TimeToLive"/> is set.
    /// </param>
    /// <param name="configure">Optional further settings, such as a TTL or the key prefix.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// <para>
    /// Registers <see cref="IConversationStore"/> only when none is registered yet, so a store registered by the
    /// host before this call is kept.
    /// </para>
    /// <para>
    /// Keys start with <see cref="DaprConversationStoreOptions.KeyPrefix"/>, after the component's own key prefix
    /// (the app id by default), so conversations are per app and can share the workflow state store. Setting
    /// <see cref="DaprConversationStoreOptions.TimeToLive"/> is recommended: it also removes the rare documents
    /// an append leaves behind when it cannot confirm whether its commit landed.
    /// </para>
    /// </remarks>
    public static IAgentsBuilder WithConversationStore(
        this IAgentsBuilder builder,
        string stateStoreName,
        Action<DaprConversationStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateStoreName);

        var options = new DaprConversationStoreOptions { StateStoreName = stateStoreName };
        configure?.Invoke(options);
        options.Validate();

        AgentComponentRegistrar.GetServices(builder).TryAddSingleton<IConversationStore>(sp => new DaprConversationStore(
            new DaprConversationStateBackend(sp.GetRequiredService<DaprStateManagementClient>(), options),
            options,
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetService<ILogger<DaprConversationStore>>()));

        return builder;
    }

    // =========================================================================
    // Context window and conversation compaction
    //
    // WithContextWindow caps what each LLM call sends to what fits the model; it changes nothing that
    // is stored. WithConversationCompaction summarizes a stored conversation's older turns once it grows,
    // and saves the summary in the store, so it is computed once rather than on every call.
    // =========================================================================

    /// <summary>
    /// Sets the context window of every agent's model, so each LLM call leaves out earlier turns, whole and oldest
    /// first, until it fits. The stored history is not changed.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="maxContextTokens">The model's context window, in tokens.</param>
    /// <param name="maxOutputTokens">The tokens to keep free for the model's reply.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// The budget is <paramref name="maxContextTokens"/> - <paramref name="maxOutputTokens"/>. A turn starts at a user
    /// message, so the history sent never opens with an assistant message or a tool result, and a tool call always
    /// travels with its results. The instructions, the context providers' messages, system messages in the history
    /// other than a stored summary, and the run's own messages are always sent; when they alone exceed the budget,
    /// the LLM call fails, and with it the run, with an error saying the context window was exceeded. Tokens are
    /// estimated as characters / 4 over the messages as they are sent.
    /// </remarks>
    public static IAgentsBuilder WithContextWindow(this IAgentsBuilder builder, int maxContextTokens, int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ContextWindowRegistration.Validate(maxContextTokens, maxOutputTokens);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ContextWindowRegistration(null, maxContextTokens, maxOutputTokens));

        return builder;
    }

    /// <summary>
    /// Sets the context window of the named agent's model, overriding the default set by
    /// <see cref="WithContextWindow(IAgentsBuilder, int, int)"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent.</param>
    /// <param name="maxContextTokens">The model's context window, in tokens.</param>
    /// <param name="maxOutputTokens">The tokens to keep free for the model's reply.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithContextWindow(
        this IAgentsBuilder builder, string agentName, int maxContextTokens, int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ContextWindowRegistration.Validate(maxContextTokens, maxOutputTokens);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ContextWindowRegistration(agentName, maxContextTokens, maxOutputTokens));

        return builder;
    }

    /// <summary>
    /// Compacts every agent's stored conversations: once the history a run loads is estimated above
    /// <see cref="ConversationCompactionOptions.TriggerTokens"/>, the turns before the most recent
    /// <see cref="ConversationCompactionOptions.KeepRecentTurns"/> are summarized after the run, and later runs
    /// load the summary in their place.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="configure">Sets the options; <see cref="ConversationCompactionOptions.TriggerTokens"/> is required.</param>
    /// <returns>The agents builder.</returns>
    /// <remarks>
    /// Applies to runs against a stored conversation (<c>WithConversationStore(...)</c>). The summary is appended to
    /// the conversation and the original messages stay stored. Compaction is best effort: if the summary call fails,
    /// the run still succeeds and the next run tries again.
    /// </remarks>
    public static IAgentsBuilder WithConversationCompaction(
        this IAgentsBuilder builder, Action<ConversationCompactionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ConversationCompactionRegistration(null, BuildCompactionOptions(configure)));

        return builder;
    }

    /// <summary>
    /// Compacts the named agent's stored conversations, overriding the default set by
    /// <see cref="WithConversationCompaction(IAgentsBuilder, Action{ConversationCompactionOptions})"/>.
    /// </summary>
    /// <param name="builder">The agents builder.</param>
    /// <param name="agentName">The name of the agent.</param>
    /// <param name="configure">Sets the options; <see cref="ConversationCompactionOptions.TriggerTokens"/> is required.</param>
    /// <returns>The agents builder.</returns>
    public static IAgentsBuilder WithConversationCompaction(
        this IAgentsBuilder builder, string agentName, Action<ConversationCompactionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(configure);

        AgentComponentRegistrar.GetServices(builder).AddSingleton(new ConversationCompactionRegistration(agentName, BuildCompactionOptions(configure)));

        return builder;
    }

    private static ConversationCompactionOptions BuildCompactionOptions(Action<ConversationCompactionOptions> configure)
    {
        var options = new ConversationCompactionOptions();
        configure(options);
        options.Validate();
        return options;
    }
}
