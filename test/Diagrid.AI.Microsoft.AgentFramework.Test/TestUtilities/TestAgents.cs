using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

internal sealed class TestAgentThread : AgentSession
{
    public TestAgentThread()
    {
    }
}

internal sealed class TestAIAgent : AIAgent
{
    private readonly Func<IEnumerable<ChatMessage>, AgentResponse> _responseFactory;

    public TestAIAgent(string name, Func<IEnumerable<ChatMessage>, AgentResponse>? responseFactory = null)
    {
        SetAgentName(name);
        _responseFactory = responseFactory ?? new Func<IEnumerable<ChatMessage>, AgentResponse>(_ => AgentRunResponseFactory.CreateWithText("{}"));
    }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentSession>(new TestAgentThread());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentSession>(new TestAgentThread());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
        ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session,
        AgentRunOptions? options, CancellationToken cancellationToken) =>
        Task.FromResult(_responseFactory(messages));

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    private void SetAgentName(string name)
    {
        var type = typeof(AIAgent);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        var prop = type.GetProperty("Name", flags);
        if (prop is not null)
        {
            if (prop.CanWrite)
            {
                prop.SetValue(this, name);
                return;
            }

            var backingField = type.GetField("<Name>k__BackingField", flags);
            if (backingField is { FieldType: { } } && backingField.FieldType == typeof(string))
            {
                backingField.SetValue(this, name);
                return;
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.FieldType != typeof(string))
            {
                continue;
            }

            if (field.Name.Contains("name", StringComparison.OrdinalIgnoreCase))
            {
                field.SetValue(this, name);
                return;
            }
        }
    }
}
