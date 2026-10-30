// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations.Internal;

public sealed class ConversationKeysTests
{
    [Theory]
    [InlineData("abc", "p:abc")]
    [InlineData("a|b", "p:a%7Cb")]
    [InlineData("a#b", "p:a%23b")]
    [InlineData("a%7Cb", "p:a%257Cb")]
    public void ManifestKey_PrefixesTheId_AndEscapesTheReservedCharacters(string conversationId, string expected) =>
        Assert.Equal(expected, ConversationKeys.ManifestKey("p:", conversationId));

    [Fact]
    public void ManifestKey_KeepsDistinctIdsDistinct() =>
        Assert.NotEqual(ConversationKeys.ManifestKey("p:", "a|b"), ConversationKeys.ManifestKey("p:", "a%7Cb"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ManifestKey_RejectsABlankId(string conversationId) =>
        Assert.Throws<ArgumentException>(() => ConversationKeys.ManifestKey("p:", conversationId));

    [Fact]
    public void ManifestKey_RejectsAnIdLongerThanTheLimit()
    {
        var id = new string('a', DaprConversationStoreOptions.MaxConversationIdLength);
        Assert.Equal("p:" + id, ConversationKeys.ManifestKey("p:", id));
        Assert.Throws<ArgumentException>(() => ConversationKeys.ManifestKey("p:", id + "a"));
    }

    [Fact]
    public void ChunkKey_AppendsTheChunkIdAfterAHash() =>
        Assert.Equal("p:abc#c1", ConversationKeys.ChunkKey("p:abc", "c1"));

    [Theory]
    [InlineData("a|")]
    [InlineData("a#")]
    public void OptionsValidate_RejectsAKeyPrefixWithAReservedCharacter(string prefix)
    {
        var options = new DaprConversationStoreOptions { StateStoreName = "s", KeyPrefix = prefix };
        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal(nameof(DaprConversationStoreOptions.KeyPrefix), ex.ParamName);
    }
}
