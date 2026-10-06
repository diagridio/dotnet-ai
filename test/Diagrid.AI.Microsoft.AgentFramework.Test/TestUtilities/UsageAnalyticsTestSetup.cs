using System.Runtime.CompilerServices;
using Diagrid.AI.Microsoft.AgentFramework.Telemetry;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.TestUtilities;

/// <summary>
/// Keeps the many tests that call <c>AddDaprAgents</c> from ever reaching the network.
/// </summary>
/// <remarks>
/// Runs once, before any test in this assembly, via <see cref="ModuleInitializerAttribute"/>.
/// The <c>UsageAnalyticsTests</c> cases that exercise real opt-in/opt-out behavior install
/// their own recording <see cref="UsageAnalytics.Sender"/> and temporarily clear the
/// opt-out variable inside a try/finally, restoring both afterward, so this baseline is
/// always what every other test — including the dozens that call <c>AddDaprAgents</c> — sees.
/// </remarks>
internal static class UsageAnalyticsTestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        UsageAnalytics.Sender = static (_, _) => Task.CompletedTask;
        Environment.SetEnvironmentVariable("DIAGRID_NO_ANALYTICS", "1");
    }
}
