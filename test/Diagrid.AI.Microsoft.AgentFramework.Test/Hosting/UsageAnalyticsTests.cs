using System.Collections.Concurrent;
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
}
