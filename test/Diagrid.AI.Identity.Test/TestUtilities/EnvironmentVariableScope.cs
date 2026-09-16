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

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// Sets environment variables for the life of a test and restores them afterwards.
/// </summary>
/// <remarks>
/// Environment variables are process-wide, so every test that touches the discovery
/// variables lives in one test class — xUnit runs the methods of a class serially.
/// </remarks>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private static readonly string[] ManagedVariables =
    [
        IdentityDiscovery.CatalystDaprHttpPortVariable,
        IdentityDiscovery.DaprHttpPortVariable,
        IdentityDiscovery.DaprHttpEndpointVariable,
        IdentityDiscovery.DaprApiTokenVariable,
        IdentityDiscovery.IssuerVariable,
        IdentityDiscovery.AudienceVariable,
    ];

    private readonly Dictionary<string, string?> _originals = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class,
    /// clearing every discovery variable and then applying <paramref name="values"/>.
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
