using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Diagrid.AI.Microsoft.AgentFramework.Telemetry;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Hosting;

[CollectionDefinition("UsageAnalytics", DisableParallelization = true)]
public sealed class UsageAnalyticsCollection
{
}

/// <summary>
/// Tests for <see cref="UsageAnalytics"/>. Serialized via the "UsageAnalytics" collection
/// (parallelization disabled) because every case mutates the process-wide
/// <see cref="UsageAnalytics.Sender"/>, <see cref="UsageAnalytics.Endpoint"/>, and
/// environment variables that <see cref="UsageAnalytics"/> reads.
/// </summary>
[Collection("UsageAnalytics")]
public sealed class UsageAnalyticsTests
{
    [Theory]
    [InlineData("DO_NOT_TRACK", "1")]
    [InlineData("DO_NOT_TRACK", "true")]
    [InlineData("DO_NOT_TRACK", "TRUE")]
    [InlineData("DO_NOT_TRACK", " yes ")]
    [InlineData("DO_NOT_TRACK", "On")]
    [InlineData("SCARF_NO_ANALYTICS", "1")]
    [InlineData("SCARF_NO_ANALYTICS", "true")]
    [InlineData("SCARF_NO_ANALYTICS", "TRUE")]
    [InlineData("SCARF_NO_ANALYTICS", " yes ")]
    [InlineData("SCARF_NO_ANALYTICS", "On")]
    [InlineData("DIAGRID_NO_ANALYTICS", "1")]
    [InlineData("DIAGRID_NO_ANALYTICS", "true")]
    [InlineData("DIAGRID_NO_ANALYTICS", "TRUE")]
    [InlineData("DIAGRID_NO_ANALYTICS", " yes ")]
    [InlineData("DIAGRID_NO_ANALYTICS", "On")]
    public async Task Report_DoesNotSend_WhenOptOutVariableIsTruthy(string variable, string value)
    {
        using var scope = new EnvironmentVariableScope((variable, value));
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package);

            await AssertNotSentAsync(tcs, TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Theory]
    [InlineData("DO_NOT_TRACK", "0")]
    [InlineData("DO_NOT_TRACK", "false")]
    [InlineData("DO_NOT_TRACK", "no")]
    [InlineData("DO_NOT_TRACK", "off")]
    [InlineData("DO_NOT_TRACK", "")]
    [InlineData("SCARF_NO_ANALYTICS", "0")]
    [InlineData("SCARF_NO_ANALYTICS", "false")]
    [InlineData("SCARF_NO_ANALYTICS", "no")]
    [InlineData("SCARF_NO_ANALYTICS", "off")]
    [InlineData("SCARF_NO_ANALYTICS", "")]
    [InlineData("DIAGRID_NO_ANALYTICS", "0")]
    [InlineData("DIAGRID_NO_ANALYTICS", "false")]
    [InlineData("DIAGRID_NO_ANALYTICS", "no")]
    [InlineData("DIAGRID_NO_ANALYTICS", "off")]
    [InlineData("DIAGRID_NO_ANALYTICS", "")]
    public async Task Report_Sends_WhenOptOutVariableIsFalsyOrUnset(string variable, string value)
    {
        using var scope = new EnvironmentVariableScope((variable, value));
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package);

            await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public async Task Report_DoesNotSend_WhenEndpointIsEmpty()
    {
        var package = UniquePackageName();
        var originalEndpoint = UsageAnalytics.Endpoint;
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Endpoint = string.Empty;
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package);

            await AssertNotSentAsync(tcs, TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            UsageAnalytics.Endpoint = originalEndpoint;
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public async Task Report_SendsOncePerPackagePerProcess()
    {
        using var scope = new EnvironmentVariableScope();
        var packageA = UniquePackageName();
        var packageB = UniquePackageName();
        var sentUris = new ConcurrentQueue<Uri>();
        var secondSendTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalSender = UsageAnalytics.Sender;
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                sentUris.Enqueue(uri);
                if (sentUris.Count >= 2)
                {
                    secondSendTcs.TrySetResult(true);
                }

                return Task.CompletedTask;
            };

            UsageAnalytics.Report(packageA, new Dictionary<string, string?> { ["kind"] = "agent" });
            UsageAnalytics.Report(packageA, new Dictionary<string, string?> { ["kind"] = "agent" });
            UsageAnalytics.Report(packageB);

            var completed = await Task.WhenAny(secondSendTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(secondSendTcs.Task, completed);

            // Give a moment for a wrongful third send (a duplicate for packageA) to arrive
            // before asserting the final count.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }

        Assert.Equal(2, sentUris.Count);
        Assert.Contains(sentUris, uri => uri.Query.Contains($"package={packageA}", StringComparison.Ordinal));
        Assert.Contains(sentUris, uri => uri.Query.Contains($"package={packageB}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Report_BuildsUrl_WithExpectedDimensions()
    {
        using var scope = new EnvironmentVariableScope();
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package, new Dictionary<string, string?>
            {
                ["kind"] = "agent",
                ["framework"] = "MicrosoftAgentFramework",
            });

            var sent = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
            var query = sent.Query;

            Assert.Contains($"package={package}", query);
            Assert.Contains("version=", query);
            Assert.Contains("os=", query);
            Assert.Contains("arch=", query);
            Assert.Contains("dotnet_version=", query);
            Assert.Contains("target=dapr", query);
            Assert.Contains("ci=false", query);
            Assert.Contains("kind=agent", query);
            Assert.Contains("framework=MicrosoftAgentFramework", query);
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public async Task Report_CallerDimensionsOverrideDefaultsAndDropEmpty()
    {
        using var scope = new EnvironmentVariableScope();
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package, new Dictionary<string, string?>
            {
                ["target"] = "catalyst",
                ["ci"] = "",
                ["framework"] = null,
            });

            var sent = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
            var query = sent.Query;

            Assert.Contains("target=catalyst", query);
            Assert.DoesNotContain("ci=", query);
            Assert.DoesNotContain("framework=", query);
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public async Task Report_TrimsAndBoundsDimensionValues()
    {
        using var scope = new EnvironmentVariableScope();
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var longValue = "  " + new string('x', 200) + "  ";
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package, new Dictionary<string, string?> { ["framework"] = longValue });

            var sent = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
            var query = sent.Query;

            Assert.Contains($"framework={new string('x', 64)}", query);
            Assert.DoesNotContain(new string('x', 65), query);
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Theory]
    [InlineData(null, null, null, "dapr")]
    [InlineData(null, "http://localhost:3500", null, "dapr")]
    [InlineData("https://grpc-prj1.api.cloud.diagrid.io:443", null, null, "catalyst")]
    [InlineData(null, "https://http-prj1.api.cloud.diagrid.io", null, "catalyst")]
    [InlineData("https://notdiagrid.io:443", null, null, "dapr")]
    [InlineData(null, null, "diagrid://abc", "catalyst")]
    [InlineData(null, null, "   ", "dapr")]
    [InlineData("grpc-prj1.api.cloud.diagrid.io:443", null, null, "catalyst")]
    [InlineData("https://diagrid.io", null, null, "catalyst")]
    public async Task Report_DetectsTarget(string? grpcEndpoint, string? httpEndpoint, string? apiToken, string expected)
    {
        using var scope = new EnvironmentVariableScope(
            ("DAPR_GRPC_ENDPOINT", grpcEndpoint),
            ("DAPR_HTTP_ENDPOINT", httpEndpoint),
            ("DAPR_API_TOKEN", apiToken));

        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package);

            var sent = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
            Assert.Contains($"target={expected}", sent.Query);
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Theory]
    [InlineData("CI", "true", true)]
    [InlineData("CI", "0", false)]
    [InlineData("GITHUB_ACTIONS", "true", true)]
    [InlineData("GITLAB_CI", "true", true)]
    [InlineData("CIRCLECI", "true", true)]
    [InlineData("TRAVIS", "true", true)]
    [InlineData("TF_BUILD", "True", true)]
    [InlineData("BUILDKITE", "true", true)]
    [InlineData("BUILDKITE", "", false)]
    [InlineData("JENKINS_URL", "https://ci.example.invalid/", true)]
    public async Task Report_DetectsCi(string variable, string value, bool expected)
    {
        using var scope = new EnvironmentVariableScope((variable, value));
        var package = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package);

            var sent = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
            Assert.Equal(expected, sent.Query.Contains("ci=true", StringComparison.Ordinal));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public async Task Report_SwallowsSenderException_AndReturns()
    {
        using var scope = new EnvironmentVariableScope();
        var throwingPackage = UniquePackageName();
        var followUpPackage = UniquePackageName();
        var originalSender = UsageAnalytics.Sender;
        try
        {
            UsageAnalytics.Sender = (_, _) => throw new InvalidOperationException("boom");

            var exception = Record.Exception(() => UsageAnalytics.Report(throwingPackage));
            Assert.Null(exception);

            // Give the fire-and-forget task a moment to run the throwing sender.
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            var followUpException = Record.Exception(() => UsageAnalytics.Report(followUpPackage));
            Assert.Null(followUpException);

            await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
        }
    }

    [Fact]
    public void Report_WithNullDimensions_DoesNotThrow()
    {
        // Ambient DIAGRID_NO_ANALYTICS=1 (set by the assembly's module initializer) keeps
        // this from reaching the network; only the "does not throw" behavior is under test.
        var package = UniquePackageName();

        var exception = Record.Exception(() => UsageAnalytics.Report(package, null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ScarfUrl_HasTheDocumentedShape()
    {
        using var scope = new EnvironmentVariableScope();
        UsageAnalytics.ResetForTests();
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalSender = UsageAnalytics.Sender;
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report("Diagrid.AI.Microsoft.AgentFramework", new Dictionary<string, string?>
            {
                ["os"] = "linux",
                ["arch"] = "x64",
                ["target"] = "dapr",
                ["ci"] = "false",
                ["kind"] = "agent",
                ["framework"] = "AgentFramework",
                ["framework_version"] = "1.0.0",
            });

            var uri = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));

            // Captured from the unmodified class. Only the two machine/build-dependent
            // values are substituted: version (MinVer, changes every commit) and
            // dotnet_version (the runtime patch). Everything else is the literal.
            var packageVersion = typeof(UsageAnalytics).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            Assert.Equal(
                "https://diagrid.gateway.scarf.sh/dotnet-ai?arch=x64&ci=false" +
                $"&dotnet_version={Environment.Version}" +
                "&framework=AgentFramework&framework_version=1.0.0&kind=agent&os=linux" +
                "&package=Diagrid.AI.Microsoft.AgentFramework&target=dapr" +
                $"&version={packageVersion}",
                uri.AbsoluteUri);
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.ResetForTests();
        }
    }

    [Fact]
    public async Task Report_SendsToEachAllowedDestination()
    {
        using var scope = new EnvironmentVariableScope();
        var sent = new ConcurrentQueue<Uri>();
        await WithDestinationsAsync(
            [Fake("one", "https://one.invalid/x"), Fake("two", "https://two.invalid/x")],
            sent,
            async () =>
            {
                UsageAnalytics.Report(UniquePackageName());
                await WaitForCountAsync(sent, 2);
            });

        Assert.Equal(["one.invalid", "two.invalid"], sent.Select(u => u.Host).OrderBy(h => h, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Report_SkipsADestinationWhoseGateIsClosed()
    {
        using var scope = new EnvironmentVariableScope();
        var sent = new ConcurrentQueue<Uri>();
        await WithDestinationsAsync(
            [Fake("one", "https://one.invalid/x", allowed: () => false), Fake("two", "https://two.invalid/x")],
            sent,
            async () =>
            {
                UsageAnalytics.Report(UniquePackageName());
                await WaitForCountAsync(sent, 1);
                await Task.Delay(300);
            });

        Assert.Equal(["two.invalid"], sent.Select(u => u.Host));
    }

    [Fact]
    public async Task Report_DedupesPerDestinationPerPackage()
    {
        using var scope = new EnvironmentVariableScope();
        var sent = new ConcurrentQueue<Uri>();
        var oneOpen = false;
        var package = UniquePackageName();
        await WithDestinationsAsync(
            [Fake("one", "https://one.invalid/x", allowed: () => oneOpen), Fake("two", "https://two.invalid/x")],
            sent,
            async () =>
            {
                // First call: "one" is gated shut, "two" sends.
                UsageAnalytics.Report(package);
                await WaitForCountAsync(sent, 1);

                // Second call: "one" opens. It must still get its single send, because the
                // closed gate did not consume it, while "two" must not send again.
                oneOpen = true;
                UsageAnalytics.Report(package);
                UsageAnalytics.Report(package);
                await WaitForCountAsync(sent, 2);
                await Task.Delay(300);
            });

        Assert.Equal(["one.invalid", "two.invalid"], sent.Select(u => u.Host).OrderBy(h => h, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Report_ADestinationThatThrows_DoesNotStopTheOthers()
    {
        using var scope = new EnvironmentVariableScope();
        var sent = new ConcurrentQueue<Uri>();
        await WithDestinationsAsync(
            [
                Fake("gate-throws", "https://a.invalid/x", allowed: () => throw new InvalidOperationException("boom")),
                new UsageAnalytics.Destination("build-throws", "https://b.invalid/x", () => true, (_, _, _) => throw new InvalidOperationException("boom")),
                Fake("ok", "https://ok.invalid/x"),
            ],
            sent,
            async () =>
            {
                var exception = Record.Exception(() => UsageAnalytics.Report(UniquePackageName()));
                Assert.Null(exception);
                await WaitForCountAsync(sent, 1);
                await Task.Delay(300);
            });

        Assert.Equal(["ok.invalid"], sent.Select(u => u.Host));
    }

    [Fact]
    public async Task Report_EmptyEndpointOnADestination_IsANoOpForThatDestinationOnly()
    {
        using var scope = new EnvironmentVariableScope();
        var sent = new ConcurrentQueue<Uri>();
        await WithDestinationsAsync(
            [Fake("one", string.Empty), Fake("two", "https://two.invalid/x")],
            sent,
            async () =>
            {
                UsageAnalytics.Report(UniquePackageName());
                await WaitForCountAsync(sent, 1);
                await Task.Delay(300);
            });

        Assert.Equal(["two.invalid"], sent.Select(u => u.Host));
    }

    [Fact]
    public async Task Report_WritesNothingToConsole_WhenEndpointIsUnreachable()
    {
        // The class has no logger by design, and a faulted unobserved Task is the one
        // remaining way telemetry noise can reach a host application.
        using var scope = new EnvironmentVariableScope();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalSender = UsageAnalytics.Sender;
        var originalEndpoint = UsageAnalytics.Endpoint;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            UsageAnalytics.Sender = null;

            // Port 1 on loopback refuses the connection immediately.
            UsageAnalytics.Endpoint = "http://127.0.0.1:1/unreachable";
            UsageAnalytics.Report(UniquePackageName());

            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.Endpoint = originalEndpoint;
        }

        Assert.Equal(string.Empty, captured.ToString());
    }

    [Fact]
    public void Endpoint_StillProxiesToTheScarfDestination()
    {
        // The existing seam. The original tests depend on it and must not be edited.
        var original = UsageAnalytics.Endpoint;
        try
        {
            UsageAnalytics.Endpoint = "https://example.invalid/x";
            Assert.Equal(
                "https://example.invalid/x",
                UsageAnalytics.Destinations.Single(d => d.Name == "scarf").Endpoint);
        }
        finally
        {
            UsageAnalytics.Endpoint = original;
        }
    }

    [Fact]
    public void ReoGate_Open_OnACleanDeveloperMachine()
    {
        using var scope = GateScope();
        ClearAllGateVars();
        Assert.True(UsageAnalytics.ReoAllowed());
    }

    [Theory]
    [InlineData("DO_NOT_TRACK")]
    [InlineData("SCARF_NO_ANALYTICS")]
    [InlineData("DIAGRID_NO_ANALYTICS")]
    public void ReoGate_Closed_OnOptOut(string variable)
    {
        using var scope = GateScope();
        ClearAllGateVars();
        Environment.SetEnvironmentVariable(variable, "1");
        Assert.False(UsageAnalytics.ReoAllowed());
    }

    [Fact]
    public void ReoGate_Closed_InKubernetes()
    {
        using var scope = GateScope();
        ClearAllGateVars();
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "10.0.0.1");
        Assert.False(UsageAnalytics.ReoAllowed());
    }

    [Theory]
    [InlineData("DAPR_API_TOKEN", "t")]
    [InlineData("DAPR_GRPC_ENDPOINT", "https://grpc.abc.diagrid.io:443")]
    [InlineData("DAPR_HTTP_ENDPOINT", "https://http.abc.diagrid.io")]
    public void ReoGate_Closed_WhenPointedAtCatalyst(string variable, string value)
    {
        using var scope = GateScope();
        ClearAllGateVars();
        Environment.SetEnvironmentVariable(variable, value);
        Assert.False(UsageAnalytics.ReoAllowed());
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("GITLAB_CI")]
    [InlineData("CIRCLECI")]
    [InlineData("TRAVIS")]
    [InlineData("TF_BUILD")]
    public void ReoGate_Closed_InCi(string variable)
    {
        using var scope = GateScope();
        ClearAllGateVars();
        Environment.SetEnvironmentVariable(variable, "true");
        Assert.False(UsageAnalytics.ReoAllowed());
    }

    [Fact]
    public void ReoGate_FailsClosed_OnMalformedEndpoint()
    {
        // DAPR_GRPC_ENDPOINT is user-supplied; DetectTarget parses it as a URI.
        using var scope = GateScope();
        ClearAllGateVars();
        Environment.SetEnvironmentVariable("DAPR_GRPC_ENDPOINT", "http://[:::::");
        Assert.False(UsageAnalytics.ReoAllowed());
    }

    [Fact]
    public void ReoGate_IsNotVacuous_UnderCi()
    {
        // Guards the Review Focus trap: the suite runs with CI=true in GitHub Actions.
        using var scope = GateScope();
        Environment.SetEnvironmentVariable("CI", "true");
        Assert.False(UsageAnalytics.ReoAllowed());
        ClearAllGateVars();
        Assert.True(UsageAnalytics.ReoAllowed());
    }

    [Theory]
    [InlineData("1.2.3+abc123def", "1.2.3")]
    [InlineData("1.2.3-preview.1+abc123def", "1.2.3-preview.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("+abc", "unknown")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void PackageVersion_TrimsBuildMetadataAndKeepsPrerelease(string? raw, string expected)
    {
        Assert.Equal(expected, UsageAnalytics.PackageVersionFromRaw(raw));
    }

    [Fact]
    public void PackageVersion_ReadsTheInformationalVersionOfThePackageAssembly()
    {
        var expected = typeof(UsageAnalytics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.Equal(expected, UsageAnalytics.PackageVersion());
    }

    [Fact]
    public void Version_IsUnknown_WhenAttributeIsAbsent()
    {
        // The trimmer can strip AssemblyInformationalVersionAttribute entirely.
        var bare = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NoInformationalVersion"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);

        Assert.Equal("unknown", UsageAnalytics.PackageVersionFrom(bare));
        Assert.Equal("unknown", UsageAnalytics.PackageVersionFrom(null));
    }

    [Fact]
    public void Dotnet_IsUnknown_WhenDescriptionIsUnavailable()
    {
        Assert.Equal("unknown", UsageAnalytics.DotnetVersionFrom(() => throw new PlatformNotSupportedException()));
        Assert.Equal("unknown", UsageAnalytics.DotnetVersionFrom(() => null));
        Assert.Equal("unknown", UsageAnalytics.DotnetVersionFrom(() => "  "));
    }

    [Fact]
    public void Dotnet_ReadsTheRuntimeVersion()
    {
        Assert.Equal(Environment.Version.ToString(), UsageAnalytics.DotnetVersion());
    }

    [Fact]
    public void Dotnet_IsTrimmedAndCapped()
    {
        Assert.Equal("8.0.11", UsageAnalytics.DotnetVersionFrom(() => " 8.0.11 "));
        Assert.Equal(64, UsageAnalytics.DotnetVersionFrom(() => new string('9', 200)).Length);
    }

    [Fact]
    public async Task ScarfUrl_IsUnchanged_WhenReoDimensionsAreAdded()
    {
        // The Review Focus case: Reo's `dotnet` key must not reach the shared dimension
        // dictionary. Scarf keeps sending version= and dotnet_version= exactly as before.
        using var scope = new EnvironmentVariableScope();
        UsageAnalytics.ResetForTests();
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalSender = UsageAnalytics.Sender;
        try
        {
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report("Diagrid.AI.Microsoft.AgentFramework", new Dictionary<string, string?>
            {
                ["os"] = "linux",
                ["arch"] = "x64",
                ["target"] = "dapr",
                ["ci"] = "false",
                ["kind"] = "agent",
                ["framework"] = "AgentFramework",
                ["framework_version"] = "1.0.0",
            });

            var uri = await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));

            // Same golden value as ScarfUrl_HasTheDocumentedShape (kept in one helper here
            // because that test builds its literal inline and must not be edited).
            Assert.Equal(ScarfGolden(), uri.AbsoluteUri);
            Assert.DoesNotContain("dotnet=", uri.Query);
            Assert.Equal(
                ["arch", "ci", "dotnet_version", "framework", "framework_version", "kind", "os", "package", "target", "version"],
                uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')[0]).ToArray());
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.ResetForTests();
        }
    }

    private static string ScarfGolden()
    {
        var packageVersion = typeof(UsageAnalytics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        return "https://diagrid.gateway.scarf.sh/dotnet-ai?arch=x64&ci=false" +
            $"&dotnet_version={Environment.Version}" +
            "&framework=AgentFramework&framework_version=1.0.0&kind=agent&os=linux" +
            "&package=Diagrid.AI.Microsoft.AgentFramework&target=dapr" +
            $"&version={packageVersion}";
    }

    /// <summary>
    /// Saves and restores every variable the gate reads, including
    /// <c>KUBERNETES_SERVICE_HOST</c>, which <see cref="EnvironmentVariableScope"/> does not manage.
    /// </summary>
    private static EnvironmentVariableScope GateScope() =>
        new(("KUBERNETES_SERVICE_HOST", null));

    private static void ClearAllGateVars()
    {
        string[] names =
        [
            "DO_NOT_TRACK", "SCARF_NO_ANALYTICS", "DIAGRID_NO_ANALYTICS",
            "CI", "GITHUB_ACTIONS", "GITLAB_CI", "CIRCLECI", "TRAVIS", "TF_BUILD",
            "BUILDKITE", "JENKINS_URL",
            "KUBERNETES_SERVICE_HOST",
            "DAPR_GRPC_ENDPOINT", "DAPR_HTTP_ENDPOINT", "DAPR_API_TOKEN",
        ];
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static UsageAnalytics.Destination Fake(string name, string endpoint, Func<bool>? allowed = null) =>
        new(name, endpoint, allowed ?? (() => true), (e, package, _) => new Uri($"{e}?package={package}"));

    private static async Task WithDestinationsAsync(
        IReadOnlyList<UsageAnalytics.Destination> destinations,
        ConcurrentQueue<Uri> sent,
        Func<Task> body)
    {
        var originalDestinations = UsageAnalytics.Destinations;
        var originalSender = UsageAnalytics.Sender;
        try
        {
            UsageAnalytics.Destinations = [.. destinations];
            UsageAnalytics.Sender = (uri, _) =>
            {
                sent.Enqueue(uri);
                return Task.CompletedTask;
            };

            await body();
        }
        finally
        {
            UsageAnalytics.Destinations = originalDestinations;
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.ResetForTests();
        }
    }

    private static async Task WaitForCountAsync(ConcurrentQueue<Uri> sent, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (sent.Count < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(sent.Count >= count, $"expected {count} sends, saw {sent.Count}");
    }

    // Short on purpose: every dimension value, the package name included, is capped at
    // 64 characters by the reporter, and a name that carries the test method plus a full
    // GUID overruns that cap and no longer matches the assertions.
    private static string UniquePackageName() => $"test-{Guid.NewGuid():N}";

    private static async Task<Uri> WaitForSendAsync(TaskCompletionSource<Uri> tcs, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        Assert.Same(tcs.Task, completed);
        return await tcs.Task;
    }

    private static async Task AssertNotSentAsync(TaskCompletionSource<Uri> tcs, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        Assert.NotSame(tcs.Task, completed);
    }

    // ---- Reo destination -------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string?> ReoCallerDimensions = new Dictionary<string, string?>
    {
        ["kind"] = "agent",
        ["framework"] = "MicrosoftAgentFramework",
        ["framework_version"] = "1.2.3",
    };

    /// <summary>
    /// Reports once through the real destination list with Scarf switched off and Reo
    /// pointed at a local address, and returns the URIs the Sender saw. Never reaches the
    /// live telemetry host: the Sender captures, and the endpoint is loopback regardless.
    /// </summary>
    private static async Task<Uri> CaptureReoUriAsync(
        string package,
        IReadOnlyDictionary<string, string?>? dimensions)
    {
        using var scope = GateScope();
        UsageAnalytics.ResetForTests();
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalSender = UsageAnalytics.Sender;
        var originalEndpoint = UsageAnalytics.Endpoint;
        var originalReoEndpoint = UsageAnalytics.ReoEndpoint;
        try
        {
            UsageAnalytics.Endpoint = string.Empty;
            UsageAnalytics.ReoEndpoint = "http://127.0.0.1:1/data";
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(package, dimensions);
            return await WaitForSendAsync(tcs, TimeSpan.FromSeconds(2));
        }
        finally
        {
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.Endpoint = originalEndpoint;
            UsageAnalytics.ReoEndpoint = originalReoEndpoint;
            UsageAnalytics.ResetForTests();
        }
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

    [Fact]
    public void ReoEndpoint_DefaultsToTheDocumentedHost()
    {
        // The test assembly's module initializer blanks the live endpoint, so assert the
        // shipped default through its constant.
        Assert.Equal("https://telemetry.diagrid.io/data", UsageAnalytics.DefaultReoEndpoint);
        Assert.Contains(UsageAnalytics.Destinations, d => d.Name == "reo");
    }

    [Fact]
    public async Task ReoUrl_MapsCollectedNamesToRegisteredKeys()
    {
        var package = UniquePackageName();
        var uri = await CaptureReoUriAsync(package, ReoCallerDimensions);
        var query = ParseQuery(uri);

        Assert.Equal("nuget_runtime", query["activity_type"]);
        Assert.Equal(package, query["product"]);
        Assert.Equal(UsageAnalytics.PackageVersion(), query["version"]);
        Assert.Equal(Environment.Version.ToString(), query["dotnet"]);
        Assert.Equal(
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "darwin" : "unknown",
            query["platform"]);
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), query["arch"]);
        Assert.Equal("agent", query["kind"]);
        Assert.Equal("MicrosoftAgentFramework", query["framework"]);
        Assert.Equal("1.2.3", query["framework_version"]);

        // Exactly the nine registered keys: anything else is silently discarded by Reo.
        Assert.Equal(
            ["activity_type", "arch", "dotnet", "framework", "framework_version", "kind", "platform", "product", "version"],
            query.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ReoUrl_OmitsTargetAndCi()
    {
        var uri = await CaptureReoUriAsync(UniquePackageName(), new Dictionary<string, string?>
        {
            ["kind"] = "agent",
            ["target"] = "dapr",
            ["ci"] = "false",
            ["os"] = "linux",
        });
        var query = ParseQuery(uri);

        foreach (var key in new[] { "target", "ci", "os", "package", "dotnet_version" })
        {
            Assert.DoesNotContain(key, query.Keys);
        }
    }

    [Fact]
    public async Task ReoUrl_CarriesVersionAndDotnet()
    {
        var uri = await CaptureReoUriAsync(UniquePackageName(), ReoCallerDimensions);
        var query = ParseQuery(uri);

        Assert.False(string.IsNullOrEmpty(query["version"]));
        Assert.False(string.IsNullOrEmpty(query["dotnet"]));
        Assert.Equal(UsageAnalytics.DotnetVersion(), query["dotnet"]);
    }

    [Fact]
    public async Task ReoUrl_DropsEmptyAndNullDimensions()
    {
        var uri = await CaptureReoUriAsync(UniquePackageName(), new Dictionary<string, string?>
        {
            ["kind"] = "agent",
            ["framework"] = null,
            ["framework_version"] = "   ",
        });
        var query = ParseQuery(uri);

        Assert.Equal("agent", query["kind"]);
        Assert.DoesNotContain("framework", query.Keys);
        Assert.DoesNotContain("framework_version", query.Keys);
    }

    [Fact]
    public async Task ReoUrl_EscapesHostileValues()
    {
        var uri = await CaptureReoUriAsync(UniquePackageName(), new Dictionary<string, string?>
        {
            ["kind"] = "a&b=c",
            ["framework"] = "x y#z",
        });
        var query = ParseQuery(uri);

        Assert.Equal("a&b=c", query["kind"]);
        Assert.Equal("x y#z", query["framework"]);
        Assert.Equal(8, query.Count);
        Assert.Equal(string.Empty, uri.Fragment);
    }

    [Fact]
    public async Task ReoUrl_CapsLongValues()
    {
        var uri = await CaptureReoUriAsync(UniquePackageName(), new Dictionary<string, string?>
        {
            ["kind"] = new string('k', 200),
        });

        Assert.Equal(64, ParseQuery(uri)["kind"].Length);
    }

    [Fact]
    public async Task ReoRequest_IsAGetWithQueryParameters()
    {
        // The Sender only sees the Uri, so assert what is observable here: every field is
        // in the query string, the path is /data, and there is no fragment. The method is
        // pinned by ReoUrl_SendsNoAuthorizationHeader, which reads the request line off a
        // real socket.
        var uri = await CaptureReoUriAsync(UniquePackageName(), ReoCallerDimensions);

        Assert.Equal("/data", uri.AbsolutePath);
        Assert.Equal(string.Empty, uri.Fragment);
        Assert.Equal(9, ParseQuery(uri).Count);
    }

    [Fact]
    public async Task ReoUrl_SendsNoAuthorizationHeader()
    {
        // Goes through the real send path (Sender cleared) to a local socket, and reads the
        // raw request: the request line proves GET, the headers prove no credentials.
        using var scope = GateScope();
        UsageAnalytics.ResetForTests();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var originalSender = UsageAnalytics.Sender;
        var originalEndpoint = UsageAnalytics.Endpoint;
        var originalReoEndpoint = UsageAnalytics.ReoEndpoint;
        try
        {
            UsageAnalytics.Sender = null;
            UsageAnalytics.Endpoint = string.Empty;
            UsageAnalytics.ReoEndpoint = $"http://127.0.0.1:{port}/data";

            var package = UniquePackageName();
            UsageAnalytics.Report(package, ReoCallerDimensions);

            using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                if (read == 0)
                {
                    break;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));

            var lines = request.ToString().Split("\r\n");
            Assert.StartsWith("GET /data?", lines[0], StringComparison.Ordinal);
            Assert.Contains("activity_type=nuget_runtime", lines[0], StringComparison.Ordinal);
            Assert.DoesNotContain(lines, l => l.StartsWith("Authorization", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lines, l => l.StartsWith("X-Api-Key", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.StartsWith("User-Agent: ", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            listener.Stop();
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.Endpoint = originalEndpoint;
            UsageAnalytics.ReoEndpoint = originalReoEndpoint;
            UsageAnalytics.ResetForTests();
        }
    }

    [Fact]
    public async Task ReoUrl_IsNotSent_WhenTheGateIsClosed()
    {
        using var scope = new EnvironmentVariableScope(("KUBERNETES_SERVICE_HOST", "10.0.0.1"));
        UsageAnalytics.ResetForTests();
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalSender = UsageAnalytics.Sender;
        var originalEndpoint = UsageAnalytics.Endpoint;
        var originalReoEndpoint = UsageAnalytics.ReoEndpoint;
        try
        {
            UsageAnalytics.Endpoint = string.Empty;
            UsageAnalytics.ReoEndpoint = "http://127.0.0.1:1/data";
            UsageAnalytics.Sender = (uri, _) =>
            {
                tcs.TrySetResult(uri);
                return Task.CompletedTask;
            };

            UsageAnalytics.Report(UniquePackageName(), ReoCallerDimensions);
            await AssertNotSentAsync(tcs, TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", null);
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.Endpoint = originalEndpoint;
            UsageAnalytics.ReoEndpoint = originalReoEndpoint;
            UsageAnalytics.ResetForTests();
        }
    }

    [Fact]
    public async Task AddDaprAgents_ReturnsImmediately_WhenEndpointHangs()
    {
        // AddDaprAgents is on a host application's startup path. The listener accepts
        // connections into its backlog and never replies.
        using var scope = GateScope();
        UsageAnalytics.ResetForTests();
        var hang = new TcpListener(IPAddress.Loopback, 0);
        hang.Start();
        var port = ((IPEndPoint)hang.LocalEndpoint).Port;
        var originalSender = UsageAnalytics.Sender;
        var originalEndpoint = UsageAnalytics.Endpoint;
        var originalReoEndpoint = UsageAnalytics.ReoEndpoint;
        try
        {
            UsageAnalytics.Sender = null;
            UsageAnalytics.Endpoint = string.Empty;

            // Warm the JIT and DI plumbing with Reo off, so only the timed call can send.
            UsageAnalytics.ReoEndpoint = string.Empty;
            new ServiceCollection().AddDaprAgents();
            UsageAnalytics.ResetForTests();
            UsageAnalytics.ReoEndpoint = $"http://127.0.0.1:{port}/data";

            var sw = Stopwatch.StartNew();
            new ServiceCollection().AddDaprAgents();
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds}ms");

            // Let the one second HttpClient timeout end the background send before the
            // listener goes away, so nothing lingers into the next test.
            await Task.Delay(TimeSpan.FromMilliseconds(1300));
        }
        finally
        {
            hang.Stop();
            UsageAnalytics.Sender = originalSender;
            UsageAnalytics.Endpoint = originalEndpoint;
            UsageAnalytics.ReoEndpoint = originalReoEndpoint;
            UsageAnalytics.ResetForTests();
        }
    }
}
