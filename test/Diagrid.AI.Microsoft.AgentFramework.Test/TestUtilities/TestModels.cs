using System.Text.Json.Serialization;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

public sealed record TestPayload(string? Value);

[JsonSerializable(typeof(TestPayload))]
public partial class TestJsonContext : JsonSerializerContext
{
}

public sealed record CamelCasePayload(string? Answer, double Confidence);

// No [JsonPropertyName] on CamelCasePayload: the naming policy must come from the
// context's own options, which is what consumers rely on.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CamelCasePayload))]
public partial class CamelCaseJsonContext : JsonSerializerContext
{
}
