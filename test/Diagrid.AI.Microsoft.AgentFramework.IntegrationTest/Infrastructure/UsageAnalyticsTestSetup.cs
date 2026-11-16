using System.Runtime.CompilerServices;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

internal static class UsageAnalyticsTestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // DaprFixture calls AddDaprAgents(), which reports usage. On a developer machine
        // (no CI variable, target dapr) that would send a real event to the live Reo and
        // Scarf hosts. Opt out for the whole assembly, before any fixture runs.
        Environment.SetEnvironmentVariable("DIAGRID_NO_ANALYTICS", "1");
    }
}
