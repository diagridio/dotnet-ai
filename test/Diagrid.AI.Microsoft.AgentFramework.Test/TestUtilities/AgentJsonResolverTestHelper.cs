using System.Reflection;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using System.Text.Json.Serialization.Metadata;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

public static class AgentJsonResolverTestHelper
{
    private static readonly FieldInfo ResolverField = typeof(AgentJsonResolverAccessor)
        .GetField("_resolver", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Unable to locate resolver field.");

    public static void Reset() => ResolverField.SetValue(null, null);

    public sealed class StubResolver(JsonTypeInfo? typeInfo) : IAgentJsonTypeInfoResolver
    {
        private readonly JsonTypeInfo? _typeInfo = typeInfo;

        public JsonTypeInfo<T>? GetTypeInfo<T>() => _typeInfo as JsonTypeInfo<T>;
    }
}
