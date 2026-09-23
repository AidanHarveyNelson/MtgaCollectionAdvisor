using MtgaCollectionAdvisor.Core.Creators;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// The descriptions here are shaped like the real ones surveyed in the spike: prose and
/// links around a pasted Arena export, decorated headers, or a single deck-site link.
/// </summary>
public class VideoDeckExtractorTests
{
    [Fact]
    public void Extract_Should_ReturnInlineList_When_DescriptionHasArenaExport()
    {
        var description = $"""
            Join this channel to get access to perks!
            https://aetherhub.com/Deck/Edit/1426379
            Deck
            {MainLines(15)}
            Like and subscribe for weekly videos!
            """;

        var source = VideoDeckExtractor.Extract(description);

        Assert.Equal(DeckSourceKind.InlineList, source.Kind);
        var cards = ArenaDeckListParser.Parse(source.Decklist!);
        Assert.Equal(60, cards.Where(c => c.Board == DeckBoard.Main).Sum(c => c.Quantity));
        Assert.DoesNotContain("subscribe", source.Decklist!);
    }

    [Fact]
    public void Extract_Should_KeepSideboardSeparate_When_HeaderIsDecorated()
    {
        var description = $"""
            🃏 DECKLIST
            {MainLines(15)}

            🧰 SIDEBOARD
            2 Ghost Vacuum (DSK) 248
            1 Thor, God of Thunder (MSH) 156

            📌 DESCRIPCIÓN DEL DECK
            """;

        var cards = ArenaDeckListParser.Parse(VideoDeckExtractor.Extract(description).Decklist!);

        Assert.Equal(60, cards.Where(c => c.Board == DeckBoard.Main).Sum(c => c.Quantity));
        Assert.Equal(3, cards.Where(c => c.Board == DeckBoard.Sideboard).Sum(c => c.Quantity));
    }

    [Fact]
    public void Extract_Should_IgnoreShortNumberedLists()
    {
        var description = """
            Here are 5 uncommons you should try!
            1 Ajani's Anguish
            2 Yoshimaru, Beloved Companion
            3 Hall of Echoes
            4 Mabel, Bitter Recluse
            5 Sphinx of False Conclusions
            """;

        Assert.Equal(DeckSourceKind.None, VideoDeckExtractor.Extract(description).Kind);
    }

    [Fact]
    public void Extract_Should_ReturnArchidektId_When_DescriptionLinksArchidekt()
    {
        var source = VideoDeckExtractor.Extract(
            "Decklist: https://archidekt.com/decks/9876543/historic_dragons\nMoxfield: https://moxfield.com/decks/abc");

        Assert.Equal(DeckSourceKind.Archidekt, source.Kind);
        Assert.Equal(9876543, source.ArchidektId);
    }

    [Theory]
    [InlineData("https://aetherhub.com/Deck/Public/1234567", "AetherHub")]
    [InlineData("https://moxfield.com/decks/Ab3dEfGh", "Moxfield")]
    [InlineData("https://mtga.untapped.gg/meta/decks/12/mono-red", "Untapped")]
    [InlineData("https://www.mtggoldfish.com/deck/6543210", "MTGGoldfish")]
    public void Extract_Should_NameExternalSite_When_OnlyAnUnreadableSiteIsLinked(string url, string site)
    {
        var source = VideoDeckExtractor.Extract($"Today's deck!\nDecklist: {url}\nThanks for watching");

        Assert.Equal(DeckSourceKind.External, source.Kind);
        Assert.Equal(site, source.ExternalSite);
        Assert.Equal(url, source.ExternalUrl);
    }

    [Fact]
    public void Extract_Should_PreferInlineList_Over_Links()
    {
        var description = $"""
            Deck link: https://aetherhub.com/Deck/ranked-264-hymn-y-baubles
            Deck
            {MainLines(15)}
            """;

        Assert.Equal(DeckSourceKind.InlineList, VideoDeckExtractor.Extract(description).Kind);
    }

    [Fact]
    public void Extract_Should_ReturnNone_When_DescriptionHasNoDeck()
    {
        var source = VideoDeckExtractor.Extract(
            "Step into the vibrant world of Alchi. Subscribe for more gameplay deep dives!");

        Assert.Same(VideoDeckSource.None, source);
    }

    /// <summary>Fifteen 4-ofs: a 60-card mainboard with the set codes Arena exports.</summary>
    private static string MainLines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => $"4 Test Card {(char)('A' + i)} (TST) {i}"));
}
