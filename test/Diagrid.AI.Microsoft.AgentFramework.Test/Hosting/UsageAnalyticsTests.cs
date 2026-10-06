using System.Collections.Concurrent;
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
