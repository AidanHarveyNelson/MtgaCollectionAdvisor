using MtgaCollectionAdvisor.Core.Analysis;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class DeckFilterTests
{
    private static readonly WildcardInventory EmptyWallet = WildcardInventory.Empty;

    [Fact]
    public void Apply_Should_KeepDeck_When_ItContainsEveryRequiredCard()
    {
        var deck = Deck("Boros", "WR", cards: ["Lightning Bolt", "Mountain", "Plains"]);
        var criteria = new DeckFilterCriteria { ContainsCards = ["Lightning Bolt", "Plains"] };

        var result = DeckFilter.Apply([deck], criteria, EmptyWallet);

        Assert.Single(result);
    }

    [Fact]
    public void Apply_Should_DropDeck_When_ItIsMissingOneRequiredCard()
    {
        var deck = Deck("Boros", "WR", cards: ["Lightning Bolt", "Mountain"]);
        var criteria = new DeckFilterCriteria { ContainsCards = ["Lightning Bolt", "Sheoldred"] };

        var result = DeckFilter.Apply([deck], criteria, EmptyWallet);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_Should_DropDeck_When_ItContainsAnyExcludedCard()
    {
        var deck = Deck("Mono Black", "B", cards: ["Swamp", "Sheoldred"]);
        var criteria = new DeckFilterCriteria { ExcludesCards = ["Sheoldred"] };

        var result = DeckFilter.Apply([deck], criteria, EmptyWallet);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_Should_MatchCardNames_CaseInsensitively()
    {
        var deck = Deck("Burn", "R", cards: ["Lightning Bolt"]);
        var criteria = new DeckFilterCriteria { ContainsCards = ["lightning bolt"] };

        var result = DeckFilter.Apply([deck], criteria, EmptyWallet);

        Assert.Single(result);
    }

    [Fact]
    public void Apply_Should_MatchSideboardCards()
    {
        var deck = Deck("Burn", "R", cards: ["Mountain"], sideboard: ["Abrade"]);
        var criteria = new DeckFilterCriteria { ContainsCards = ["Abrade"] };

        var result = DeckFilter.Apply([deck], criteria, EmptyWallet);

        Assert.Single(result);
    }

    [Fact]
    public void Apply_Should_CombineCardFilters_WithColorAndBudgetFilters()
    {
        var cheapBoros = Deck("Cheap Boros", "WR", cards: ["Lightning Bolt"], rares: 2);
        var expensiveBoros = Deck("Expensive Boros", "WR", cards: ["Lightning Bolt"], rares: 40);
        var cheapMono = Deck("Cheap Mono Red", "R", cards: ["Lightning Bolt"], rares: 1);

        var criteria = new DeckFilterCriteria
        {
            Colors = new HashSet<char> { 'W' },
            MaxWildcards = 10,
            ContainsCards = ["Lightning Bolt"]
        };

        var result = DeckFilter.Apply([cheapBoros, expensiveBoros, cheapMono], criteria, EmptyWallet);

        Assert.Single(result);
        Assert.Equal("Cheap Boros", result[0].Deck.Name);
    }

    [Fact]
    public void Apply_Should_ReturnEverything_When_CriteriaIsEmpty()
    {
        var decks = new[] { Deck("A", "W", ["Plains"]), Deck("B", "U", ["Island"]) };

        var result = DeckFilter.Apply(decks, new DeckFilterCriteria(), EmptyWallet);

        Assert.Equal(2, result.Count);
        Assert.True(new DeckFilterCriteria().IsEmpty);
    }

    [Fact]
    public void Apply_Should_KeepOnlyExactColorCombination_When_ExactColorsIsSet()
    {
        var boros = Deck("Boros", "WR", ["Plains"]);
        var mono = Deck("Mono White", "W", ["Plains"]);

        var criteria = new DeckFilterCriteria
        {
            Colors = new HashSet<char> { 'W' },
            ExactColors = true
        };

        var result = DeckFilter.Apply([boros, mono], criteria, EmptyWallet);

        Assert.Single(result);
        Assert.Equal("Mono White", result[0].Deck.Name);
    }

    [Fact]
    public void Apply_Should_KeepOnlyAffordableDecks_When_OnlyCraftableIsSet()
    {
        var affordable = Deck("Affordable", "R", ["Mountain"], rares: 2);
        var tooExpensive = Deck("Too Expensive", "R", ["Mountain"], rares: 9);
        var wallet = new WildcardInventory(0, 0, Rares: 4, Mythics: 0);

        var result = DeckFilter.Apply(
            [affordable, tooExpensive], new DeckFilterCriteria { OnlyCraftable = true }, wallet);

        Assert.Single(result);
        Assert.Equal("Affordable", result[0].Deck.Name);
    }

    [Fact]
    public void Apply_Should_DropUnplayableDecks_When_IncludeUnplayableIsFalse()
    {
        var playable = Deck("Playable", "R", ["Mountain"]);
        var unplayable = Deck("Not On Arena", "R", ["Mountain"], unavailableOnArena: ["Mosswood Dreadknight"]);

        var result = DeckFilter.Apply([playable, unplayable], new DeckFilterCriteria(), EmptyWallet);

        Assert.Single(result);
        Assert.Equal("Playable", result[0].Deck.Name);
    }

    [Fact]
    public void Apply_Should_DropIllegalDecks_When_IncludeUnplayableIsFalse()
    {
        var legal = Deck("Legal", "R", ["Mountain"]);
        var illegal = Deck("Rotated Out", "R", ["Mountain"], illegalInFormat: ["Lightning Bolt"]);

        var result = DeckFilter.Apply([legal, illegal], new DeckFilterCriteria(), EmptyWallet);

        Assert.Single(result);
        Assert.Equal("Legal", result[0].Deck.Name);
    }

    [Fact]
    public void Apply_Should_KeepUnplayableDecks_When_IncludeUnplayableIsSet()
    {
        var unavailable = Deck("Not On Arena", "R", ["Mountain"], unavailableOnArena: ["Mosswood Dreadknight"]);
        var illegal = Deck("Rotated Out", "R", ["Mountain"], illegalInFormat: ["Lightning Bolt"]);

        var criteria = new DeckFilterCriteria { IncludeUnplayable = true };
        var result = DeckFilter.Apply([unavailable, illegal], criteria, EmptyWallet);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_Should_StillNarrowUnplayableDecks_When_OtherFiltersAreSet()
    {
        // Keeping unplayable decks must not exempt them from the user's actual filters.
        var boros = Deck("Boros", "WR", ["Plains"], unavailableOnArena: ["Some Card"]);
        var mono = Deck("Mono Blue", "U", ["Island"], unavailableOnArena: ["Some Card"]);

        var criteria = new DeckFilterCriteria
        {
            IncludeUnplayable = true,
            Colors = new HashSet<char> { 'W' }
        };

        var result = DeckFilter.Apply([boros, mono], criteria, EmptyWallet);

        Assert.Single(result);
        Assert.Equal("Boros", result[0].Deck.Name);
    }

    [Fact]
    public void IsEmpty_Should_BeTrue_When_OnlyIncludeUnplayableIsSet()
    {
        // Which list is being shown is not a filter the user picked, so it must not
        // offer them a "clear filters" link.
        Assert.True(new DeckFilterCriteria { IncludeUnplayable = true }.IsEmpty);
    }

    private static DeckAnalysisResult Deck(
        string name,
        string colors,
        IReadOnlyList<string> cards,
        IReadOnlyList<string>? sideboard = null,
        int rares = 0,
        IReadOnlyList<string>? unavailableOnArena = null,
        IReadOnlyList<string>? illegalInFormat = null)
    {
        var refs = cards.Select(c => new DeckCardRef(c, 4, DeckBoard.Main))
            .Concat((sideboard ?? []).Select(c => new DeckCardRef(c, 2, DeckBoard.Sideboard)))
            .ToList();

        var candidate = new CandidateDeck(
            SourceId: $"test:{name}",
            Name: name,
            Url: "",
            FormatKey: Formats.Standard.Key,
            Popularity: 0,
            Cards: refs,
            FetchedAt: DateTimeOffset.UtcNow);

        return new DeckAnalysisResult(
            Deck: candidate,
            Needed: new WildcardNeed(0, 0, rares, 0),
            OwnedCopies: 0,
            TotalCopies: refs.Sum(c => c.Quantity),
            Gaps: [],
            UnavailableOnArena: unavailableOnArena ?? [],
            IllegalInFormat: illegalInFormat ?? [],
            Colors: colors);
    }
}
