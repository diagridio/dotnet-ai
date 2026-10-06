// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Agents.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Telemetry;

/// <summary>
/// Anonymous usage reporting for this package.
/// </summary>
/// <remarks>
/// <para>
/// NuGet publishes aggregate download counts only. This class reports one event per
/// package per process, with the package version and the host platform, so Diagrid can
/// see which versions run, and where. No application data is collected. See the "Usage
/// analytics" section of the README, including how to opt out.
/// </para>
/// <para>
/// One event per process means one event per replica per restart on Kubernetes. The
/// numbers count process starts, not deployments or users.
/// </para>
/// <para>
/// The call never blocks and never throws: the send runs on a background task and every
/// failure is swallowed there. The one second <see cref="HttpClient.Timeout"/> bounds the
/// HTTP call itself, not DNS resolution, so the background task can outlive a second on a
/// network that blackholes lookups. The caller never waits on it either way. Blocked
/// egress and air-gapped clusters are normal conditions, not faults.
/// </para>
/// <para>
/// There is no logger at this static level, and this type never writes to the console or
/// any other output: adding one would risk surfacing noise in a host application that
/// never asked for it. Set <see cref="Endpoint"/> to an empty string to turn this class
/// into a no-op.
/// </para>
/// </remarks>
internal static class UsageAnalytics
{
    /// <summary>
    /// The cross-ecosystem "do not track" convention, Scarf's own variable, and a
    /// Diagrid-specific opt-out. Any of them set to a truthy value disables reporting.
    /// </summary>
    private static readonly string[] OptOutEnvironmentVariables =
    [
        "DO_NOT_TRACK",
        "SCARF_NO_ANALYTICS",
        "DIAGRID_NO_ANALYTICS",
    ];

    /// <summary>
    /// "CI" is the convention most vendors follow. The rest cover vendors that set their
    /// own flag but not "CI".
    /// </summary>
    private static readonly string[] CiTruthyEnvironmentVariables =
    [
        "CI",
        "GITHUB_ACTIONS",
        "GITLAB_CI",
        "CIRCLECI",
        "TRAVIS",
        "TF_BUILD",
    ];

    /// <summary>
    /// Vendors that set a value rather than a flag. Presence is enough.
    /// </summary>
    private static readonly string[] CiPresenceEnvironmentVariables =
    [
        "BUILDKITE",
        "JENKINS_URL",
    ];

    /// <summary>
    /// The Dapr SDK variables that point a process at Catalyst.
    /// </summary>
    private static readonly string[] DaprEndpointEnvironmentVariables =
    [
        "DAPR_GRPC_ENDPOINT",
        "DAPR_HTTP_ENDPOINT",
    ];

    private static readonly HashSet<string> TruthyValues = new(StringComparer.Ordinal)
    {
        "1",
        "true",
        "yes",
        "on",
    };

    private const string CatalystHostSuffix = "diagrid.io";
    private const int MaxDimensionLength = 64;

    private static readonly object ReportedPackagesLock = new();
    private static readonly HashSet<string> ReportedPackages = new(StringComparer.Ordinal);

    // Shared for the lifetime of the process, matching the general guidance to never
    // create a short-lived HttpClient per call. The one second timeout bounds each event
    // so a blocked or air-gapped network never keeps the background task alive long.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Gets or sets the Scarf event-collection endpoint that receives usage events. The
    /// route only records the request and redirects nowhere. Set to an empty string to
    /// turn usage reporting into a no-op, including in tests.
    /// </summary>
    internal static string Endpoint { get; set; } = "https://diagrid.gateway.scarf.sh/dotnet-ai";

    /// <summary>
    /// Gets or sets a test hook that replaces the default HTTP GET. <see langword="null"/>
    /// (the default) sends a real request through the shared <see cref="HttpClient"/>.
    /// </summary>
    internal static Func<Uri, CancellationToken, Task>? Sender { get; set; }

    /// <summary>
    /// Clears the per-process "already reported" guard, so a test can call
    /// <see cref="Report"/> again for a package name it already used.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (ReportedPackagesLock)
        {
            ReportedPackages.Clear();
        }
    }

    /// <summary>
    /// Reports one anonymous usage event for <paramref name="package"/>, once per package
    /// per process, without ever blocking or throwing.
    /// </summary>
    /// <param name="package">
    /// The package name reported as the <c>package</c> dimension and used as the
    /// per-process "already reported" key.
    /// </param>
    /// <param name="dimensions">
    /// Extra query-string dimensions, for example <c>kind</c> and <c>framework</c>. A
    /// dimension here overrides a default of the same name; a <see langword="null"/> or
    /// empty value drops the dimension entirely, including a default. May be
    /// <see langword="null"/>.
    /// </param>
    internal static void Report(string package, IReadOnlyDictionary<string, string?>? dimensions = null)
    {
        try
        {
            var endpoint = Endpoint;
            if (string.IsNullOrEmpty(endpoint))
            {
                return;
            }

            lock (ReportedPackagesLock)
            {
                if (!ReportedPackages.Add(package))
                {
                    return;
                }
            }

            if (IsReportingDisabled())
            {
                return;
            }

            // Copy the caller's dictionary before crossing to the background task: the
            // caller owns the instance they passed in and may reuse or mutate it.
            var capturedDimensions = dimensions is null
                ? null
                : new Dictionary<string, string?>(dimensions, StringComparer.Ordinal);

            _ = Task.Run(() => SendEventAsync(package, endpoint, capturedDimensions));
        }
        catch
        {
            // Usage reporting must never break the host application.
        }
    }

    /// <summary>
    /// Returns the installed version of the <c>Microsoft.Agents.AI</c> package (the
    /// library behind the Microsoft Agent Framework integration), or <see langword="null"/>
    /// when it cannot be determined. Used for the <c>framework_version</c> dimension.
    /// </summary>
    internal static string? FrameworkVersion()
    {
        try
        {
            // AIAgent is a public type this project already references from
            // Microsoft.Agents.AI, so its assembly is the framework's own assembly.
            return CleanInformationalVersion(typeof(AIAgent).Assembly);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SendEventAsync(string package, string endpoint, IReadOnlyDictionary<string, string?>? dimensions)
    {
        try
        {
            var uri = BuildUrl(endpoint, package, dimensions);
            var sender = Sender;
            if (sender is not null)
            {
                await sender(uri, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", $"{package}/{PackageVersion()}");
            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Fire-and-forget: blocked egress and air-gapped clusters are normal
            // conditions, not faults, so every failure here is swallowed silently.
        }
    }

    private static Uri BuildUrl(string endpoint, string package, IReadOnlyDictionary<string, string?>? dimensions)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["package"] = package,
            ["version"] = PackageVersion(),
            ["os"] = DetectOperatingSystem(),
            ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ["dotnet_version"] = Environment.Version.ToString(),
            ["target"] = DetectTarget(),
            ["ci"] = IsRunningInCi() ? "true" : "false",
        };

        if (dimensions is not null)
        {
            foreach (var pair in dimensions)
            {
                if (pair.Value is null)
                {
                    parameters.Remove(pair.Key);
                    continue;
                }

                var cleaned = Clean(pair.Value);
                if (cleaned.Length == 0)
                {
                    parameters.Remove(pair.Key);
                    continue;
                }

                parameters[pair.Key] = cleaned;
            }
        }

        var query = string.Join(
            "&",
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(Clean(pair.Value))}"));

        return new Uri($"{endpoint}?{query}");
    }

    private static string PackageVersion() => CleanInformationalVersion(typeof(UsageAnalytics).Assembly) ?? "unknown";

    /// <summary>
    /// Returns <paramref name="assembly"/>'s <see cref="AssemblyInformationalVersionAttribute"/>
    /// value with anything from the first '+' onward stripped (MinVer appends the commit
    /// hash there), or <see langword="null"/> when the attribute is absent or empty.
    /// </summary>
    private static string? CleanInformationalVersion(Assembly assembly)
    {
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        var version = plusIndex >= 0 ? raw[..plusIndex] : raw;
        return string.IsNullOrEmpty(version) ? null : version;
    }

    private static string DetectOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "darwin";
        }

        return "unknown";
    }

    /// <summary>
    /// Returns <c>catalyst</c> when the process points at Diagrid Catalyst, else
    /// <c>dapr</c>.
    /// </summary>
    /// <remarks>
    /// Catalyst is configured through the Dapr SDK variables: the endpoint host is under
    /// <c>diagrid.io</c>, and Catalyst issues <c>DAPR_API_TOKEN</c>. A self-hosted sidecar
    /// with API token authentication also reads as <c>catalyst</c>. That is an
    /// approximation, and the dashboard reads it as one.
    /// </remarks>
    private static string DetectTarget()
    {
        foreach (var name in DaprEndpointEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var host = ExtractHost(value.Trim());
            if (host is not null &&
                (string.Equals(host, CatalystHostSuffix, StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith("." + CatalystHostSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                return "catalyst";
            }
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAPR_API_TOKEN")))
        {
            return "catalyst";
        }

        return "dapr";
    }

    private static string? ExtractHost(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        // No scheme (e.g. "grpc-prj1.api.cloud.diagrid.io:443"): prepend one and retry.
        return Uri.TryCreate("https://" + value, UriKind.Absolute, out var withScheme)
            ? withScheme.Host
            : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a well-known CI variable is set. Reported as
    /// the <c>ci</c> dimension so pipeline runs can be separated from real usage.
    /// </summary>
    private static bool IsRunningInCi()
    {
        foreach (var name in CiTruthyEnvironmentVariables)
        {
            if (IsTruthy(Environment.GetEnvironmentVariable(name)))
            {
                return true;
            }
        }

        foreach (var name in CiPresenceEnvironmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the user opted out through any supported
    /// variable.
    /// </summary>
    private static bool IsReportingDisabled()
    {
        foreach (var name in OptOutEnvironmentVariables)
        {
            if (IsTruthy(Environment.GetEnvironmentVariable(name)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTruthy(string? value) => value is not null && TruthyValues.Contains(value.Trim().ToLowerInvariant());

    private static string Clean(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > MaxDimensionLength ? trimmed[..MaxDimensionLength] : trimmed;
    }
}
