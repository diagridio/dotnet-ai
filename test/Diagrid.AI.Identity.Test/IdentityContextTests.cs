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

namespace Diagrid.AI.Identity.Test;

public sealed class IdentityContextTests : IDisposable
{
    public void Dispose() => IdentityContext.ClearCurrentToken();

    [Fact]
    public void SetCurrentToken_StoresTheToken()
    {
        using var scope = IdentityContext.SetCurrentToken("abc123");

        Assert.Equal("abc123", IdentityContext.CurrentUserToken);
    }

    [Fact]
    public void DisposingScope_RestoresPreviousToken()
    {
        using var outer = IdentityContext.SetCurrentToken("first");

        using (IdentityContext.SetCurrentToken("second"))
        {
            Assert.Equal("second", IdentityContext.CurrentUserToken);
        }

        Assert.Equal("first", IdentityContext.CurrentUserToken);
    }

    [Fact]
    public void ClearCurrentToken_RemovesTheToken()
    {
        IdentityContext.SetCurrentToken("abc123");

        IdentityContext.ClearCurrentToken();

        Assert.Null(IdentityContext.CurrentUserToken);
    }

    [Fact]
    public void CurrentUserToken_DefaultsToNull()
    {
        IdentityContext.ClearCurrentToken();

        Assert.Null(IdentityContext.CurrentUserToken);
    }

    [Fact]
    public void OutboundIdentityHeaders_CarriesBearerToken()
    {
        using var scope = IdentityContext.SetCurrentToken("tok");

        var headers = IdentityContext.OutboundIdentityHeaders();

        Assert.Equal(
            new Dictionary<string, string> { ["X-Diagrid-User-Token"] = "Bearer tok" },
            headers);
    }

    [Fact]
    public void OutboundIdentityHeaders_OmitsHeaderWithoutToken()
    {
        IdentityContext.ClearCurrentToken();

        Assert.Empty(IdentityContext.OutboundIdentityHeaders());
    }

    [Fact]
    public void OutboundIdentityHeaders_IsNotOnThePublicSurface()
    {
        // GetMethod's default binding sees public members only. Reflection is the only
        // proof available: this assembly can reference the internal member at compile time.
        var helper = typeof(IdentityContext).GetMethod(nameof(IdentityContext.OutboundIdentityHeaders));

        Assert.Null(helper);
    }

    [Fact]
    public void HeaderConstants_MatchTheCrossSdkContract()
    {
        Assert.Equal("X-Diagrid-User-Token", IdentityContext.UserTokenHeader);
        Assert.Equal("Bearer ", IdentityContext.BearerPrefix);
    }
}
