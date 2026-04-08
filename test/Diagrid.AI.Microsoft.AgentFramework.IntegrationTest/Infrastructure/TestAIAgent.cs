// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

/// <summary>
/// A controllable <see cref="AIAgent"/> whose response is supplied by a factory delegate,
/// used in place of a real LLM during integration tests.
/// </summary>
internal sealed class TestAIAgent : AIAgent
{
    private readonly Func<IEnumerable<ChatMessage>, AgentResponse> _responseFactory;

    public TestAIAgent(string name, Func<IEnumerable<ChatMessage>, AgentResponse>? responseFactory = null)
    {
        SetName(name);
        _responseFactory = responseFactory
            ?? (_ => AgentRunResponseFactory.CreateWithText("{}"));
    }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentSession>(new TestAgentSession());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentSession>(new TestAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken) =>
        Task.FromResult(_responseFactory(messages));

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// Sets <see cref="AIAgent.Name"/> via reflection because the property is not publicly settable in
    /// the version of <c>Microsoft.Agents.AI</c> referenced by this project.
    /// </summary>
    private void SetName(string name)
    {
        var type = typeof(AIAgent);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public;

        var prop = type.GetProperty("Name", flags);
        if (prop is { CanWrite: true })
        {
            prop.SetValue(this, name);
            return;
        }

        var backingField = type.GetField("<Name>k__BackingField", flags);
        if (backingField is { FieldType: var ft } && ft == typeof(string))
        {
            backingField.SetValue(this, name);
            return;
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.FieldType == typeof(string) &&
                field.Name.Contains("name", StringComparison.OrdinalIgnoreCase))
            {
                field.SetValue(this, name);
                return;
            }
        }
    }

    private sealed class TestAgentSession : AgentSession;
}
