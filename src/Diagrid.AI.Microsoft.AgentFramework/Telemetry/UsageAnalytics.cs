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

    /// <summary>The Reo endpoint events are sent to. No API key: nothing secret ships in the package.</summary>
    internal const string DefaultReoEndpoint = "https://telemetry.diagrid.io/data";

    private const string CatalystHostSuffix = "diagrid.io";
    private const int MaxDimensionLength = 64;

    private static readonly object ReportedPackagesLock = new();
    private static readonly HashSet<(string Destination, string Package)> ReportedPackages = [];

    // Shared for the lifetime of the process, matching the general guidance to never
    // create a short-lived HttpClient per call. The one second timeout bounds each event
    // so a blocked or air-gapped network never keeps the background task alive long.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// One place usage events are sent: a name, an endpoint, a gate that decides whether
    /// to send at all, and a function that turns the event into a request URI.
    /// </summary>
    internal sealed class Destination
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Destination"/> class.
        /// </summary>
        /// <param name="name">The destination name, part of the per-process dedupe key.</param>
        /// <param name="endpoint">The endpoint events are sent to. Empty turns this destination into a no-op.</param>
        /// <param name="allowed">Returns <see langword="false"/> when this destination must stay silent.</param>
        /// <param name="buildUri">Builds the request URI from the endpoint, package and caller dimensions.</param>
        internal Destination(
            string name,
            string endpoint,
            Func<bool> allowed,
            Func<string, string, IReadOnlyDictionary<string, string?>?, Uri> buildUri)
        {
            Name = name;
            Endpoint = endpoint;
            Allowed = allowed;
            BuildUri = buildUri;
        }

        /// <summary>Gets the destination name.</summary>
        internal string Name { get; }

        /// <summary>Gets or sets the endpoint. Set to an empty string to disable this destination.</summary>
        internal string Endpoint { get; set; }

        /// <summary>Gets the gate. A destination whose gate returns <see langword="false"/> sends nothing.</summary>
        internal Func<bool> Allowed { get; }

        /// <summary>Gets the function that builds the request URI.</summary>
        internal Func<string, string, IReadOnlyDictionary<string, string?>?, Uri> BuildUri { get; }
    }

    /// <summary>
    /// Gets or sets the destinations every event is sent to. Tests replace the list; it
    /// is read once per <see cref="Report"/> call.
    /// </summary>
    /// <remarks>
    /// Destinations are sent to sequentially on the background task, in list order. A hung
    /// Scarf send therefore delays the Reo send by up to the one second
    /// <see cref="HttpClient.Timeout"/>. That is bounded and off the caller's path, so it
    /// is accepted rather than parallelized.
    /// </remarks>
    internal static IReadOnlyList<Destination> Destinations { get; set; } =
    [
        new Destination("scarf", "https://diagrid.gateway.scarf.sh/dotnet-ai", () => !IsReportingDisabled(), BuildUrl),
        new Destination("reo", DefaultReoEndpoint, ReoAllowed, BuildReoUrl),
    ];

    /// <summary>
    /// Gets or sets the Scarf event-collection endpoint that receives usage events. The
    /// route only records the request and redirects nowhere. Set to an empty string to
    /// turn Scarf reporting into a no-op, including in tests. This proxies to the Scarf
    /// entry of <see cref="Destinations"/>.
    /// </summary>
    internal static string Endpoint
    {
        get => Destinations.FirstOrDefault(d => d.Name == "scarf")?.Endpoint ?? string.Empty;
        set
        {
            var scarf = Destinations.FirstOrDefault(d => d.Name == "scarf");
            if (scarf is not null)
            {
                scarf.Endpoint = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the Reo endpoint that receives <c>nuget_runtime</c> events. Set to an
    /// empty string to turn Reo reporting into a no-op, including in tests. This proxies to
    /// the Reo entry of <see cref="Destinations"/>, the way <see cref="Endpoint"/> proxies Scarf.
    /// </summary>
    internal static string ReoEndpoint
    {
        get => Destinations.FirstOrDefault(d => d.Name == "reo")?.Endpoint ?? string.Empty;
        set
        {
            var reo = Destinations.FirstOrDefault(d => d.Name == "reo");
            if (reo is not null)
            {
                reo.Endpoint = value;
            }
        }
    }

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
            // Snapshot each destination's endpoint now, matching the time-of-call
            // behaviour of the single endpoint this replaced.
            var targets = Destinations
                .Select(destination => (Destination: destination, Endpoint: destination.Endpoint))
                .Where(target => !string.IsNullOrEmpty(target.Endpoint))
                .ToArray();
            if (targets.Length == 0)
            {
                return;
            }

            // Copy the caller's dictionary before crossing to the background task: the
            // caller owns the instance they passed in and may reuse or mutate it.
            var capturedDimensions = dimensions is null
                ? null
                : new Dictionary<string, string?>(dimensions, StringComparer.Ordinal);

            _ = Task.Run(() => SendEventsAsync(package, targets, capturedDimensions));
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

    /// <summary>
    /// Returns <see langword="true"/> when an event may be sent to Reo. Stricter than
    /// Scarf's gate, which is opt-out only: all four conditions must hold, and anything
    /// that cannot be determined counts as closed.
    /// </summary>
    /// <remarks>
    /// The conditions are: the user has not opted out; <c>KUBERNETES_SERVICE_HOST</c> is
    /// unset; <see cref="DetectTarget"/> reports <c>dapr</c>; and no CI variable is set.
    /// The cluster check lives here rather than in <see cref="DetectTarget"/> because
    /// that feeds Scarf's <c>target</c> dimension, which must not change.
    /// </remarks>
    internal static bool ReoAllowed()
    {
        try
        {
            if (IsReportingDisabled())
            {
                return false;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            {
                return false;
            }

            // DetectTarget reads an unparseable endpoint as "dapr" (no host to match, so no
            // catalyst). Scarf's dimension keeps that behaviour; Reo cannot tell where such
            // a process points, so it stays closed.
            foreach (var name in DaprEndpointEnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value) && !IsParseableEndpoint(value.Trim()))
                {
                    return false;
                }
            }

            return DetectTarget() == "dapr" && !IsRunningInCi();
        }
        catch
        {
            // Fail closed: an indeterminate environment never sends to Reo.
            return false;
        }
    }

    private static async Task SendEventsAsync(
        string package,
        (Destination Destination, string Endpoint)[] targets,
        IReadOnlyDictionary<string, string?>? dimensions)
    {
        try
        {
            foreach (var (destination, endpoint) in targets)
            {
                // One destination's failure, in its gate, URI builder or send, must never
                // affect another's, and nothing may escape as an unobserved task fault.
                try
                {
                    // The gate runs before the dedupe key is taken, so a closed gate on
                    // one destination never consumes that destination's single send.
                    if (!destination.Allowed())
                    {
                        continue;
                    }

                    lock (ReportedPackagesLock)
                    {
                        if (!ReportedPackages.Add((destination.Name, package)))
                        {
                            continue;
                        }
                    }

                    await SendAsync(package, destination.BuildUri(endpoint, package, dimensions)).ConfigureAwait(false);
                }
                catch
                {
                    // Fire-and-forget: blocked egress and air-gapped clusters are normal
                    // conditions, not faults, so every failure here is swallowed silently.
                }
            }
        }
        catch
        {
            // Unreachable in practice; kept so the background task can never fault.
        }
    }

    private static async Task SendAsync(string package, Uri uri)
    {
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

    private static Uri BuildUrl(string endpoint, string package, IReadOnlyDictionary<string, string?>? dimensions)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["package"] = package,
            ["version"] = PackageVersion(),
            ["os"] = DetectOperatingSystem(),
            ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ["dotnet_version"] = DotnetVersion(),
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

        return new Uri($"{endpoint}?{EncodeQuery(parameters)}");
    }

    /// <summary>
    /// Builds the Reo request URI: the registered <c>nuget_runtime</c> keys only, mapped from
    /// the collected names. <c>version</c> and <c>dotnet</c> are read here and never enter the
    /// shared dimension dictionary, so Scarf's URL is unaffected. <c>target</c> and
    /// <c>ci</c> are deliberately absent: the gate makes both constant on every Reo event.
    /// Any key Reo has not registered is silently discarded on arrival.
    /// </summary>
    private static Uri BuildReoUrl(string endpoint, string package, IReadOnlyDictionary<string, string?>? dimensions)
    {
        var candidates = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["activity_type"] = "nuget_runtime",
            ["product"] = package,
            ["version"] = PackageVersion(),
            ["dotnet"] = DotnetVersion(),
            ["platform"] = DetectOperatingSystem(),
            ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        };

        foreach (var key in new[] { "kind", "framework", "framework_version" })
        {
            if (dimensions is not null && dimensions.TryGetValue(key, out var value))
            {
                candidates[key] = value;
            }
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in candidates)
        {
            var cleaned = pair.Value is null ? string.Empty : Clean(pair.Value);
            if (cleaned.Length > 0)
            {
                parameters[pair.Key] = cleaned;
            }
        }

        return new Uri($"{endpoint}?{EncodeQuery(parameters)}");
    }

    /// <summary>
    /// Sorts by key and percent-encodes each name and value, so a hostile value such as
    /// <c>a&amp;b=c</c> stays one parameter. Shared by every destination's builder.
    /// </summary>
    private static string EncodeQuery(Dictionary<string, string> parameters) =>
        string.Join(
            "&",
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(Clean(pair.Value))}"));

    /// <summary>
    /// The package's own informational version (build metadata after '+' trimmed,
    /// prerelease kept), or <c>"unknown"</c>. Never throws.
    /// </summary>
    internal static string PackageVersion() => PackageVersionFrom(typeof(UsageAnalytics).Assembly);

    /// <summary>
    /// <see cref="PackageVersion"/> for an arbitrary assembly. Returns <c>"unknown"</c> when
    /// the assembly is null, the attribute is absent (the trimmer can strip it) or empty,
    /// or anything throws.
    /// </summary>
    internal static string PackageVersionFrom(Assembly? assembly)
    {
        try
        {
            return assembly is null ? "unknown" : CleanInformationalVersion(assembly) ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// <see cref="PackageVersionFrom"/> for a raw informational-version string.
    /// </summary>
    internal static string PackageVersionFromRaw(string? raw) => CleanVersionString(raw) ?? "unknown";

    /// <summary>
    /// The runtime version (<see cref="Environment.Version"/>, e.g. <c>8.0.11</c>), trimmed and
    /// capped, or <c>"unknown"</c>. Never throws.
    /// </summary>
    internal static string DotnetVersion() => DotnetVersionFrom(() => Environment.Version.ToString());

    /// <summary>
    /// <see cref="DotnetVersion"/> over an injectable source. Returns <c>"unknown"</c> when the
    /// source returns null or whitespace, or throws.
    /// </summary>
    internal static string DotnetVersionFrom(Func<string?> source)
    {
        try
        {
            var value = source();
            return string.IsNullOrWhiteSpace(value) ? "unknown" : Clean(value);
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Returns <paramref name="assembly"/>'s <see cref="AssemblyInformationalVersionAttribute"/>
    /// value with anything from the first '+' onward stripped (MinVer appends the commit
    /// hash there), or <see langword="null"/> when the attribute is absent or empty.
    /// </summary>
    private static string? CleanInformationalVersion(Assembly assembly) =>
        CleanVersionString(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static string? CleanVersionString(string? raw)
    {
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

    /// <summary>
    /// Strict counterpart to <see cref="ExtractHost"/>, whose scheme-prepending retry
    /// accepts input like <c>http://[:::::</c> (it parses as host <c>http</c>). A value
    /// with a scheme must parse as it stands; one without must parse once a scheme is
    /// added. Used only by <see cref="ReoAllowed"/>.
    /// </summary>
    private static bool IsParseableEndpoint(string value)
    {
        var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host);
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
