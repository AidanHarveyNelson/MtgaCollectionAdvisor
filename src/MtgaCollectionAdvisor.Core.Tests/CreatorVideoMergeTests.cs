using MtgaCollectionAdvisor.Core.Creators;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class CreatorVideoMergeTests
{
    private static readonly CreatorChannel Alice = new("Alice", "UC-alice");
    private static readonly CreatorChannel Bob = new("Bob", "UC-bob");
    private static readonly CreatorChannel[] Curated = [Alice, Bob];

    [Fact]
    public void Merge_Should_KeepCachedVideos_When_ChannelFeedFailed()
    {
        // YouTube's feeds return 404/500 at random; that is not "Bob has no videos".
        var previous = new[] { Stored("b1", "Bob", DeckSourceKind.InlineList, decklist: "4 Shock") };

        var merged = CreatorVideoMerge.Merge(previous, [Ok(Alice), Failed(Bob)], Curated);

        var kept = Assert.Single(merged);
        Assert.Equal("b1", kept.VideoId);
        Assert.Equal("4 Shock", kept.Decklist);
    }

    [Fact]
    public void Merge_Should_ReuseStoredExtraction_When_VideoIsKnown()
    {
        var previous = new[] { Stored("a1", "Alice", DeckSourceKind.InlineList, decklist: "4 Shock", title: "Old title") };
        // The description no longer carries a list; the stored one must win regardless.
        var feed = Ok(Alice, Feed("a1", "Alice", "New title", "No deck here any more."));

        var video = Assert.Single(CreatorVideoMerge.Merge(previous, [feed], Curated));

        Assert.Equal(DeckSourceKind.InlineList, video.Kind);
        Assert.Equal("4 Shock", video.Decklist);
        Assert.Equal("New title", video.Title);
    }

    [Fact]
    public void Merge_Should_ExtractNewVideos()
    {
        var list = string.Join('\n', Enumerable.Range(1, 15).Select(i => $"4 Card {i}"));
        var feed = Ok(Alice, Feed("a2", "Alice", "Brand new", $"Deck\n{list}"));

        var video = Assert.Single(CreatorVideoMerge.Merge([], [feed], Curated));

        Assert.Equal(DeckSourceKind.InlineList, video.Kind);
        Assert.Contains("4 Card 15", video.Decklist);
    }

    [Fact]
    public void Merge_Should_LeaveArchidektPending_When_NotResolvedYet()
    {
        var previouslyFailed = Stored("a1", "Alice", DeckSourceKind.Archidekt, archidektId: 111);
        var feed = Ok(Alice,
            Feed("a1", "Alice", "Known", "https://archidekt.com/decks/111"),
            Feed("a2", "Alice", "New", "https://archidekt.com/decks/222"));

        var merged = CreatorVideoMerge.Merge([previouslyFailed], [feed], Curated);

        Assert.All(merged, v => Assert.True(v.NeedsArchidektFetch));
        Assert.Equal(222, merged.Single(v => v.VideoId == "a2").ArchidektId);
    }

    [Fact]
    public void Merge_Should_DropVideos_NoLongerInASuccessfulFeed()
    {
        var previous = new[]
        {
            Stored("old", "Alice", DeckSourceKind.None),
            Stored("still", "Alice", DeckSourceKind.None)
        };

        var merged = CreatorVideoMerge.Merge(previous, [Ok(Alice, Feed("still", "Alice", "t", ""))], Curated);

        Assert.Equal(["still"], merged.Select(v => v.VideoId));
    }

    [Fact]
    public void Merge_Should_DropCreators_NoLongerCurated()
    {
        var previous = new[] { Stored("c1", "Carol", DeckSourceKind.None) };

        var merged = CreatorVideoMerge.Merge(previous, [Failed(new CreatorChannel("Carol", "UC-carol"))], Curated);

        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_Should_TakeTheChannelLanguage_ForNewAndKnownVideos()
    {
        var pt = new CreatorChannel("Alice", "UC-alice", Language: "pt");
        var previous = new[] { Stored("known", "Alice", DeckSourceKind.None) };
        var feed = new ChannelFeedResult(pt,
        [
            Feed("known", "Alice", "t", "") with { Language = "pt" },
            Feed("new", "Alice", "t", "") with { Language = "pt" }
        ]);

        var merged = CreatorVideoMerge.Merge(previous, [feed], [pt, Bob]);

        Assert.All(merged, v => Assert.Equal("pt", v.Language));
    }

    [Fact]
    public void IsStale_Should_FollowMaxAge_When_EveryChannelHasVideos()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var both = new[] { Stored("a", "Alice", DeckSourceKind.None), Stored("b", "Bob", DeckSourceKind.None) };

        Assert.True(CreatorVideoSnapshot.Empty.IsStale(now, Curated));
        Assert.False(new CreatorVideoSnapshot(now.AddHours(-1), both).IsStale(now, Curated));
        Assert.True(new CreatorVideoSnapshot(now - CreatorVideoSnapshot.MaxAge, both).IsStale(now, Curated));
    }

    [Fact]
    public void IsStale_Should_RetrySoon_When_AChannelHasNothingCached()
    {
        // Bob's first fetch failed: waiting six hours to try again left him missing all day.
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var onlyAlice = new[] { Stored("a", "Alice", DeckSourceKind.None) };

        Assert.False(new CreatorVideoSnapshot(now.AddMinutes(-5), onlyAlice).IsStale(now, Curated));
        Assert.True(new CreatorVideoSnapshot(now - CreatorVideoSnapshot.RetryMissingAfter, onlyAlice).IsStale(now, Curated));
    }

    private static ChannelFeedResult Ok(CreatorChannel channel, params FeedVideo[] videos) => new(channel, videos);

    private static ChannelFeedResult Failed(CreatorChannel channel) => new(channel, null);

    private static FeedVideo Feed(string id, string creator, string title, string description) =>
        new(creator, id, title, new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), description);

    private static CreatorVideo Stored(
        string id, string creator, DeckSourceKind kind,
        string? decklist = null, int? archidektId = null, string title = "Title") =>
        new(id, creator, title, new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
            kind, decklist, archidektId, ExternalSite: null, ExternalUrl: null);
}
