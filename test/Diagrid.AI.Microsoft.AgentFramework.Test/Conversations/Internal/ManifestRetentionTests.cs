// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Conversations.Internal;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Conversations.Internal;

public sealed class ManifestRetentionTests
{
    private static TurnRecord Turn(string id) => new() { Id = id };

    [Fact]
    public void TrimTurns_AppendsAndKeepsTheMostRecentWindow()
    {
        var turns = ManifestRetention.TrimTurns([Turn("a"), Turn("b")], Turn("c"), 2);
        Assert.Equal(["b", "c"], turns.Select(t => t.Id));

        Assert.Equal(["a"], ManifestRetention.TrimTurns(null, Turn("a"), 2).Select(t => t.Id));
    }

    [Fact]
    public void AddSummary_KeepsTheMostRecent_AndRemembersTheNewestDropped()
    {
        var manifest = new ConversationManifest();
        for (var i = 0; i <= ConversationManifest.MaxSummaries + 1; i++)
        {
            ManifestRetention.AddSummary(manifest, new SummaryRecord { Sequence = 10 * i, CoversUpToSequence = 10 * i });
        }

        Assert.Equal(ConversationManifest.MaxSummaries, manifest.Summaries!.Count);
        Assert.Equal(20, manifest.Summaries[0].Sequence);
        Assert.Equal(10, manifest.SummariesDroppedMax);
    }

    [Fact]
    public void AddSummary_DropsNothing_BelowTheLimit()
    {
        var manifest = new ConversationManifest();
        ManifestRetention.AddSummary(manifest, new SummaryRecord { Sequence = 3, CoversUpToSequence = 3 });

        Assert.Single(manifest.Summaries!);
        Assert.Null(manifest.SummariesDroppedMax);
    }

    [Fact]
    public void TryFindSummary_ReturnsTheLatestInsideThePrefix()
    {
        var manifest = new ConversationManifest
        {
            Summaries = [new() { Sequence = 4, CoversUpToSequence = 4 }, new() { Sequence = 9, CoversUpToSequence = 9 }],
        };

        Assert.True(ManifestRetention.TryFindSummary(manifest, 9, out var inside));
        Assert.Equal(4, inside!.Sequence);
        Assert.True(ManifestRetention.TryFindSummary(manifest, 10, out var latest));
        Assert.Equal(9, latest!.Sequence);
        Assert.True(ManifestRetention.TryFindSummary(manifest, 4, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void TryFindSummary_FailsOnlyWhenThePrefixUsedADroppedSummary()
    {
        var manifest = new ConversationManifest
        {
            Summaries = [new() { Sequence = 20, CoversUpToSequence = 20 }],
            SummariesDroppedMax = 10,
        };

        Assert.True(ManifestRetention.TryFindSummary(manifest, 10, out _));
        Assert.False(ManifestRetention.TryFindSummary(manifest, 11, out _));
        Assert.True(ManifestRetention.TryFindSummary(manifest, 21, out _));
    }
}
