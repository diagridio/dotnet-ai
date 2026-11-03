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

using System.Runtime.CompilerServices;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Catalyst;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Diagrid.AI.Microsoft.AgentFramework.Hosting;

internal sealed class DaprAgentsBuilder(
    IServiceCollection services,
    WorkflowServiceCollectionExtensions.DaprWorkflowBuilder workflowBuilder) : IAgentsBuilder
{
    internal IServiceCollection Services { get; } = services;

    /// <summary>
    /// The Dapr Workflow builder <c>AddDaprAgents</c> created. Its constructor is internal to the Dapr SDK,
    /// so builder extensions such as <c>WithBinaryStorePayloads</c> can only reach it through here.
    /// </summary>
    internal WorkflowServiceCollectionExtensions.DaprWorkflowBuilder WorkflowBuilder { get; } = workflowBuilder;

    public IAgentsBuilder WithAgent(Func<IServiceProvider, AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return WithAgentCore(factory, chatClientKey: null);
    }

    public IAgentsBuilder WithAgent(string chatClientKey, Func<IServiceProvider, AIAgent> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatClientKey);
        ArgumentNullException.ThrowIfNull(factory);

        return WithAgentCore(factory, chatClientKey);
    }

    public IAgentsBuilder WithCatalyst(DiagridCatalystOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Registry.ResourceName);

        Services.AddSingleton(Options.Create(options));
        Services.AddHostedService<CatalystAgentRegistryHostedService>();

        return this;
    }

    public IAgentsBuilder WithCatalyst()
    {
        var options = new DiagridCatalystOptions();
        Services.AddSingleton(Options.Create(options));
        Services.AddHostedService<CatalystAgentRegistryHostedService>();

        return this;
    }

    private IAgentsBuilder WithAgentCore(Func<IServiceProvider, AIAgent> factory, string? chatClientKey)
    {
        return WithAgentRegistration(new AgentFactoryRegistration(WrappedFactory)
        {
            ChatClientKey = chatClientKey,
        });

        // Wrap the user's factory to extract IChatClient, instructions, and tools
        // for the per-activity workflow path.
        AIAgent WrappedFactory(IServiceProvider sp)
        {
            var agent = factory(sp);

            // If the agent was built from an IChatClient (ChatClientAgent), extract
            // the raw chat client and register it so CallLlmActivity can use it directly.
            if (agent is ChatClientAgent cca)
            {
                var rawChatClient = UnwrapFunctionInvoking(cca.ChatClient);
                AgentComponentRegistrar.RegisterAgentComponents(sp, agent, rawChatClient);
            }

            return agent;
        }
    }

    /// <summary>
    /// <paramref name="builder"/> as the <see cref="DaprAgentsBuilder"/> <c>AddDaprAgents</c> created, for the builder
    /// extensions that need more than <see cref="IAgentsBuilder"/> exposes. Any other implementation cannot provide it.
    /// </summary>
    /// <param name="builder">The agents builder an extension was called on.</param>
    /// <param name="needed">What the extension needs, completing "The agents builder does not ...".</param>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="builder"/> is not a <see cref="DaprAgentsBuilder"/>.</exception>
    internal static DaprAgentsBuilder From(IAgentsBuilder builder, string needed) =>
        builder as DaprAgentsBuilder ?? throw Unsupported(needed);

    /// <summary>
    /// The error for an agents builder that cannot provide <paramref name="needed"/>, with the message every builder
    /// extension uses: "The agents builder does not {needed}."
    /// </summary>
    /// <param name="needed">What was needed, completing "The agents builder does not ...".</param>
    /// <returns>The exception to throw.</returns>
    internal static InvalidOperationException Unsupported(string needed) =>
        new($"The agents builder does not {needed}.");

    internal IAgentsBuilder WithAgentRegistration(AgentFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Services.AddSingleton(registration);
        return this;
    }

    /// <summary>
    /// Traverses the <see cref="IChatClient"/> pipeline and returns the first client
    /// that is NOT a <see cref="FunctionInvokingChatClient"/>.
    /// This gives us the raw client suitable for single-turn LLM calls.
    /// Uses <see cref="UnsafeAccessorAttribute"/> for AOT-safe access to the protected
    /// <see cref="DelegatingChatClient.InnerClient"/> property.
    /// </summary>
    internal static IChatClient UnwrapFunctionInvoking(IChatClient client)
    {
        while (client is FunctionInvokingChatClient fic)
        {
            var inner = GetInnerClient(fic);
            if (inner is null || ReferenceEquals(inner, client))
                break;
            client = inner;
        }

        return client;
    }

    /// <summary>
    /// The client a <see cref="DelegatingChatClient"/> wraps, for walking a client pipeline.
    /// </summary>
    internal static IChatClient? GetDelegatingInnerClient(DelegatingChatClient client) =>
        GetInnerClient(client);

    /// <summary>
    /// Whether <paramref name="chatClient"/> is, or wraps at any depth, a <c>DaprChatClient</c>. Walks the pipeline
    /// here, next to <see cref="GetDelegatingInnerClient"/>, so the runtime does not need to know how it is built.
    /// </summary>
    internal static bool IsDaprChatClient(IChatClient chatClient)
    {
        var seen = new HashSet<IChatClient>(ReferenceEqualityComparer.Instance);
        for (IChatClient? client = chatClient; client is not null && seen.Add(client);)
        {
            if (client.GetType().Name == "DaprChatClient")
            {
                return true;
            }

            client = client switch
            {
                ToolResultCompatibilityChatClient wrapper => wrapper.InnerClient,
                DelegatingChatClient delegating => GetDelegatingInnerClient(delegating),
                _ => null
            };
        }

        return false;
    }

    /// <summary>
    /// Extracts the <see cref="ChatOptions"/> from a <see cref="ChatClientAgent"/> using
    /// <see cref="UnsafeAccessorAttribute"/> — AOT-safe, no runtime reflection.
    /// </summary>
    internal static ChatOptions? GetAgentChatOptions(ChatClientAgent agent) =>
        GetChatOptions(agent);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InnerClient")]
    private static extern IChatClient GetInnerClient(DelegatingChatClient client);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ChatOptions")]
    private static extern ChatOptions? GetChatOptions(ChatClientAgent agent);
}
