using System.Text.Json.Serialization;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

public sealed record TestPayload(string? Value);

[JsonSerializable(typeof(TestPayload))]
public partial class TestJsonContext : JsonSerializerContext
{
}
