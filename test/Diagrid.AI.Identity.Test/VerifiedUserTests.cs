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

using System.Collections.Immutable;

namespace Diagrid.AI.Identity.Test;

public sealed class VerifiedUserTests
{
    [Fact]
    public void HasScope_IsTrueForACarriedScope()
    {
        var user = new VerifiedUser
        {
            Subject = "alice@example.com",
            Scopes = ImmutableSortedSet.Create(StringComparer.Ordinal, "agent.invoke", "admin"),
        };

        Assert.True(user.HasScope("agent.invoke"));
        Assert.True(user.HasScope("admin"));
    }

    [Fact]
    public void HasScope_IsFalseForAScopeTheTokenDoesNotCarry()
    {
        var user = new VerifiedUser
        {
            Subject = "alice@example.com",
            Scopes = ImmutableSortedSet.Create(StringComparer.Ordinal, "agent.invoke"),
        };

        Assert.False(user.HasScope("admin.write"));
    }

    [Fact]
    public void HasScope_IsCaseSensitive()
    {
        // Scopes are opaque strings in the token; folding case here would grant a scope the
        // signer never issued.
        var user = new VerifiedUser
        {
            Subject = "alice@example.com",
            Scopes = ImmutableSortedSet.Create(StringComparer.Ordinal, "agent.invoke"),
        };

        Assert.False(user.HasScope("Agent.Invoke"));
    }

    [Fact]
    public void HasScope_OnAUserWithNoScopes_IsFalse()
    {
        var user = new VerifiedUser { Subject = "alice@example.com" };

        Assert.False(user.HasScope("agent.invoke"));
    }

    [Fact]
    public void DefaultScopes_AreAnOrdinallySortedSet()
    {
        // A handler echoing Scopes into JSON has to emit one stable order, which a hash set
        // cannot promise and a culture-sensitive comparer cannot promise across hosts.
        var user = new VerifiedUser { Subject = "alice@example.com" };

        var scopes = Assert.IsType<ImmutableSortedSet<string>>(user.Scopes);
        Assert.Empty(scopes);
        Assert.Same(StringComparer.Ordinal, scopes.KeyComparer);
    }
}
