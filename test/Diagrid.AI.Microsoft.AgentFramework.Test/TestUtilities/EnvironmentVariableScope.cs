namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

/// <summary>
/// Clears every environment variable <c>UsageAnalytics</c> reads, applies the given
/// overrides, and restores the original values on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// These variables are process-wide, and a real CI runner already sets several of them
/// (<c>CI</c>, <c>GITHUB_ACTIONS</c>). Without this scope, a test asserting <c>ci=false</c>
/// or <c>target=dapr</c> would pass locally and fail in CI. The <c>UsageAnalyticsTests</c>
/// class is marked <c>[Collection("UsageAnalytics")]</c> with parallelization disabled, so
/// these instances never race each other.
/// </remarks>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private static readonly string[] ManagedVariables =
    [
        "DO_NOT_TRACK",
        "SCARF_NO_ANALYTICS",
        "DIAGRID_NO_ANALYTICS",
        "CI",
        "GITHUB_ACTIONS",
        "GITLAB_CI",
        "CIRCLECI",
        "TRAVIS",
        "TF_BUILD",
        "BUILDKITE",
        "JENKINS_URL",
        "DAPR_GRPC_ENDPOINT",
        "DAPR_HTTP_ENDPOINT",
        "DAPR_API_TOKEN",
    ];

    private readonly Dictionary<string, string?> _originals = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class,
    /// clearing every managed variable and then applying <paramref name="values"/>.
    /// </summary>
    /// <param name="values">The variables to set for the duration of the scope.</param>
    internal EnvironmentVariableScope(params (string Name, string? Value)[] values)
    {
        foreach (var name in ManagedVariables)
        {
            _originals[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var (name, value) in values)
        {
            if (!_originals.ContainsKey(name))
            {
                _originals[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originals)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
