using Diagrid.AI.Microsoft.AgentFramework.Catalyst;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Diagrid Catalyst options.
/// </summary>
public sealed class DiagridCatalystOptions
{
	/// <summary>
	/// The version of the Catalyst agent metadata schema to write.
	/// </summary>
	public string SchemaVersion { get; init; } = "latest";

	/// <summary>
	/// The registry metadata to write for each agent. The registry resource name is also used as the Dapr state store name.
	/// </summary>
	public RegistryMetadata Registry { get; init; } = new();
}
