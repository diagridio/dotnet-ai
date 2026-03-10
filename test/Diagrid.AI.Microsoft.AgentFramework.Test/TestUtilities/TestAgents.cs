using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

internal sealed class TestAgentThread : AgentThread
{
    public TestAgentThread()
    {
    }
}

internal sealed class TestAIAgent : AIAgent
{
    private readonly Func<IEnumerable<ChatMessage>, AgentRunResponse> _responseFactory;

    public TestAIAgent(string name, Func<IEnumerable<ChatMessage>, AgentRunResponse>? responseFactory = null)
    {
        SetAgentName(name);
        _responseFactory = responseFactory ?? (_ => AgentRunResponseFactory.CreateWithText("{}"));
    }

    public override AgentThread GetNewThread() => new TestAgentThread();

    public override AgentThread DeserializeThread(JsonElement jsonElement, JsonSerializerOptions? options) =>
        new TestAgentThread();

    protected override Task<AgentRunResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentThread? thread,
        AgentRunOptions? options, CancellationToken cancellationToken) =>
        Task.FromResult(_responseFactory(messages));

    protected override async IAsyncEnumerable<AgentRunResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages, AgentThread? thread, AgentRunOptions? options,
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
