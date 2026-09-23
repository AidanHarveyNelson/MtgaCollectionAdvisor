using MtgaCollectionAdvisor.Core.Arena;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Moving decks from Arena into the app. The player picks; what they pick must land as a
/// user deck they can track, and picking it again must update it, not duplicate it.
/// </summary>
public class ArenaDeckImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<int, string> Names = new()
    {
        [100] = "Lightning Bolt",
        [300] = "Swamp",
        [400] = "Negate",
        [500] = "Lurrus of the Dream-Den",
    };

    private static readonly HashSet<string> NothingInApp = [];

    [Theory]
    [InlineData("Standard", "standard")]
    [InlineData("TraditionalStandard", "standard")]
    [InlineData("Explorer", "pioneer")]
    [InlineData("TraditionalExplorer", "pioneer")]
    [InlineData("Pioneer", "pioneer")]
    [InlineData("Historic", null)]
    [InlineData("Alchemy", null)]
    [InlineData("", null)]
    public void FormatFor_Should_MapArenaFormats(string arenaFormat, string? expectedKey)
    {
        Assert.Equal(expectedKey, ArenaDeckImport.FormatFor(arenaFormat)?.Key);
    }

    [Fact]
    public void Choices_Should_ListOwnDecks_SupportedFirst()
    {
        var choices = ArenaDeckImport.Choices([
            Deck("h", "Zombies", "Historic"),
            Deck("s", "Mono Red", "Standard"),
            Deck("w", "Precon", "Standard", wizards: true),
            Deck("e", "Lotus Field", "Explorer"),
        ], Names, NothingInApp);

        Assert.Equal(["e", "s", "h"], choices.Select(c => c.Deck.Id));   // Pioneer, Standard, then unsupported
        Assert.False(choices[2].IsSupported);
    }

    [Fact]
    public void Choices_Should_MarkDecksAlreadyInTheApp()
    {
        var inApp = new HashSet<string> { ArenaDeckImport.SourceIdFor("s") };

        var choices = ArenaDeckImport.Choices([Deck("s", "Mono Red", "Standard"), Deck("t", "Other", "Standard")], Names, inApp);

        Assert.True(choices.Single(c => c.Deck.Id == "s").InApp);
        Assert.False(choices.Single(c => c.Deck.Id == "t").InApp);
    }

    [Fact]
    public void Choices_Should_CountCards_And_UnknownCards()
    {
        var deck = Deck("s", "Mono Red", "Standard", main: [new(100, 4), new(300, 20), new(999, 2)], side: [new(400, 3)]);

        var choice = Assert.Single(ArenaDeckImport.Choices([deck], Names, NothingInApp));

        Assert.Equal(29, choice.CardCount);
        Assert.Equal(2, choice.UnknownCards);
    }

    [Fact]
    public void ToCandidateDeck_Should_MakeAUserDeck()
    {
        var deck = Deck("abc", "Mono Red", "Explorer", main: [new(100, 4), new(300, 20), new(999, 1)], side: [new(400, 2)]);

        var candidate = ArenaDeckImport.ToCandidateDeck(deck, Names, Now)!;

        Assert.True(candidate.IsUserDeck);
        Assert.Equal("manual:arena-abc", candidate.SourceId);
        Assert.Equal("Mono Red", candidate.Name);
        Assert.Equal(Formats.Pioneer.Key, candidate.FormatKey);
        Assert.Equal([
            new DeckCardRef("Lightning Bolt", 4, DeckBoard.Main),
            new DeckCardRef("Swamp", 20, DeckBoard.Main),
            new DeckCardRef("Negate", 2, DeckBoard.Sideboard),
        ], candidate.Cards);
    }

    [Fact]
    public void ToCandidateDeck_Should_NotCountTheCompanionTwice()
    {
        // As Arena logs it: the companion in its own section and again in the sideboard.
        var deck = Deck("c", "Lurrus", "Standard", main: [new(300, 60)], side: [new(500, 1)], companion: [new(500, 1)]);

        var candidate = ArenaDeckImport.ToCandidateDeck(deck, Names, Now)!;

        Assert.Equal(1, candidate.Cards.Where(c => c.Name == "Lurrus of the Dream-Den").Sum(c => c.Quantity));
        Assert.Equal(61, Assert.Single(ArenaDeckImport.Choices([deck], Names, NothingInApp)).CardCount);
    }

    [Fact]
    public void ToCandidateDeck_Should_BeNull_ForUnsupportedFormats()
    {
        Assert.Null(ArenaDeckImport.ToCandidateDeck(Deck("h", "Zombies", "Historic"), Names, Now));
    }

    private static ArenaDeck Deck(
        string id, string name, string format, bool wizards = false,
        IReadOnlyList<ArenaCard>? main = null, IReadOnlyList<ArenaCard>? side = null, IReadOnlyList<ArenaCard>? companion = null) =>
        new(id, name, format, wizards, [], companion ?? [], main ?? [new(300, 20)], side ?? []);
}
