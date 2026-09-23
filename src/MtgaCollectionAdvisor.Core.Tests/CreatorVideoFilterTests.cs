using MtgaCollectionAdvisor.Core.Creators;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class CreatorVideoFilterTests
{
    private static readonly WildcardInventory Wallet = new(0, 0, Rares: 4, Mythics: 0);

    [Fact]
    public void Apply_Should_FilterByCreator()
    {
        var cards = new[] { Card("a", "Alice"), Card("b", "Bob") };

        var result = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { Creator = "Bob" }, Wallet);

        Assert.Equal("b", Assert.Single(result).Video.VideoId);
    }

    [Fact]
    public void Apply_Should_KeepOnlyVideosWithDeck()
    {
        var cards = new[] { Card("priced", rares: 1), Card("unpriced", priced: false) };

        var result = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { OnlyWithDeck = true }, Wallet);

        Assert.Equal("priced", Assert.Single(result).Video.VideoId);
    }

    [Fact]
    public void Apply_Should_KeepOnlyCraftable()
    {
        var cards = new[] { Card("cheap", rares: 3), Card("dear", rares: 9), Card("unpriced", priced: false) };

        var result = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { OnlyCraftable = true }, Wallet);

        Assert.Equal("cheap", Assert.Single(result).Video.VideoId);
    }

    [Fact]
    public void Apply_Should_KeepOnlyAppFormats()
    {
        var cards = new[] { Card("standard"), Card("historic", illegal: 4), Card("unpriced", priced: false) };

        var result = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { OnlyAppFormats = true }, Wallet);

        Assert.Equal("standard", Assert.Single(result).Video.VideoId);
    }

    [Fact]
    public void Apply_Should_SortCheapestFirst_WithUnpricedLast()
    {
        var cards = new[]
        {
            Card("unpriced", priced: false, daysAgo: 0),
            Card("dear", rares: 9),
            Card("cheap", rares: 1)
        };

        var result = CreatorVideoFilter.Apply(
            cards, new CreatorVideoFilterCriteria { Sort = CreatorVideoSort.Cheapest }, Wallet);

        Assert.Equal(["cheap", "dear", "unpriced"], result.Select(c => c.Video.VideoId));
    }

    [Fact]
    public void Apply_Should_SortNewestFirst()
    {
        var cards = new[] { Card("old", daysAgo: 9), Card("new", daysAgo: 1), Card("mid", daysAgo: 4) };

        var result = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria(), Wallet);

        Assert.Equal(["new", "mid", "old"], result.Select(c => c.Video.VideoId));
    }

    [Fact]
    public void Apply_Should_HideNonEnglishVideos_When_NotIncluded()
    {
        var cards = new[] { Card("en"), Card("pt", language: "pt") };

        var hidden = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { IncludeNonEnglish = false }, Wallet);
        var shown = CreatorVideoFilter.Apply(cards, new CreatorVideoFilterCriteria { IncludeNonEnglish = true }, Wallet);

        Assert.Equal("en", Assert.Single(hidden).Video.VideoId);
        Assert.Equal(2, shown.Count);
    }

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("en-GB", false)]
    [InlineData("pt-BR", true)]
    [InlineData("es-ES", true)]
    public void IncludeNonEnglishByDefault_Should_FollowTheUserLanguage(string culture, bool expected)
    {
        Assert.Equal(expected,
            CreatorVideoFilterCriteria.IncludeNonEnglishByDefault(System.Globalization.CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void SuggestedDeckName_Should_CutAtSeparator()
    {
        Assert.Equal("🔴 MONO RED AGGRO BO3", Video("MONO", "🔴 MONO RED AGGRO BO3 ★ RELENTLESS SPEED | MTG Arena").SuggestedDeckName);
        Assert.Equal("Tokens!", Video("t", "Tokens! #mtg #arena").SuggestedDeckName);

        var longTitle = new string('x', 90);
        Assert.Equal(60, Video("l", longTitle).SuggestedDeckName.Length);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=v9XmRwiVUao", true)]
    [InlineData("https://youtu.be/v9XmRwiVUao", true)]
    [InlineData("https://archidekt.com/decks/123", false)]
    [InlineData("https://www.youtube.com/@Crokeyz", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVideoUrl_Should_RecogniseYouTubeWatchAndShortLinks(string? url, bool expected)
    {
        Assert.Equal(expected, CreatorVideo.IsVideoUrl(url));
    }

    private static CreatorVideoCard Card(
        string id, string creator = "Alice", bool priced = true, int rares = 0, int illegal = 0, int daysAgo = 3,
        string language = "en") =>
        new(Video(id, "Title", creator, daysAgo) with { Language = language },
            priced ? Formats.Standard : null,
            priced ? CreatorVideoPricingTests.Analysis(illegal: illegal, rares: rares) : null);

    private static CreatorVideo Video(string id, string title, string creator = "Alice", int daysAgo = 3) =>
        new(id, creator, title, new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero).AddDays(-daysAgo),
            DeckSourceKind.None, null, null, null, null);
}
