using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// The export has one job: produce text MTGA's importer accepts. Deck sources name a
/// two-faced card the Scryfall way, "Front // Back", which Arena rejects for adventures
/// and double-faced cards - it drops the line without saying which card went missing.
/// </summary>
public class ArenaDeckListWriterTests
{
    [Fact]
    public void Write_Should_UseTheFrontFace_When_CardIsAnAdventure()
    {
        var text = Write(Card("Bonecrusher Giant // Stomp", 4));

        Assert.Contains("4 Bonecrusher Giant", text);
        Assert.DoesNotContain("//", text);
    }

    [Fact]
    public void Write_Should_UseTheFrontFace_When_CardIsAModalDoubleFacedCard()
    {
        var text = Write(Card("Barkchannel Pathway // Tidechannel Pathway", 4));

        Assert.Contains("4 Barkchannel Pathway", text);
        Assert.DoesNotContain("Tidechannel", text);
    }

    [Fact]
    public void Write_Should_UseTheFrontFace_When_CardTransforms()
    {
        var text = Write(Card("Fable of the Mirror-Breaker // Reflection of Kiki-Jiki", 3));

        Assert.Contains("3 Fable of the Mirror-Breaker", text);
        Assert.DoesNotContain("Kiki-Jiki", text);
    }

    [Fact]
    public void Write_Should_UseTheFirstHalf_When_CardIsASplitCard()
    {
        // Arena takes either form for a split card, so the front face is used here too -
        // one rule for every layout beats a rule that needs to know which layout it has.
        var text = Write(Card("Cease // Desist", 2));

        Assert.Contains("2 Cease", text);
        Assert.DoesNotContain("Desist", text);
    }

    [Fact]
    public void Write_Should_LeaveOrdinaryNamesAlone()
    {
        var text = Write(Card("Lightning Bolt", 4));

        Assert.Contains("4 Lightning Bolt", text);
    }

    [Fact]
    public void Write_Should_ConvertSideboardNamesToo()
    {
        var deck = Deck([
            new DeckCardRef("Mountain", 20, DeckBoard.Main),
            new DeckCardRef("Bonecrusher Giant // Stomp", 2, DeckBoard.Sideboard)
        ]);

        var text = ArenaDeckListWriter.Write(deck);

        Assert.Contains("Sideboard", text);
        Assert.Contains("2 Bonecrusher Giant", text);
        Assert.DoesNotContain("//", text);
    }

    [Fact]
    public void Write_Should_RoundTripThroughTheParser()
    {
        // What we hand Arena must also come back into this app unchanged in meaning.
        var deck = Deck([
            new DeckCardRef("Bonecrusher Giant // Stomp", 4, DeckBoard.Main),
            new DeckCardRef("Mountain", 20, DeckBoard.Main),
            new DeckCardRef("Cease // Desist", 2, DeckBoard.Sideboard)
        ]);

        var parsed = ArenaDeckListParser.Parse(ArenaDeckListWriter.Write(deck));

        Assert.Equal(3, parsed.Count);
        Assert.Contains(parsed, c => c.Name == "Bonecrusher Giant" && c.Quantity == 4 && c.Board == DeckBoard.Main);
        Assert.Contains(parsed, c => c.Name == "Mountain" && c.Quantity == 20);
        Assert.Contains(parsed, c => c.Name == "Cease" && c.Quantity == 2 && c.Board == DeckBoard.Sideboard);
    }

    [Fact]
    public void ArenaName_Should_OnlySplitOnASeparatorWithSpaces()
    {
        // A bare "//" inside a name is not a face separator.
        Assert.Equal("Borrowing 100,000 Arrows", ArenaDeckListWriter.ArenaName("Borrowing 100,000 Arrows"));
        Assert.Equal("Fire", ArenaDeckListWriter.ArenaName("Fire // Ice"));
    }

    private static string Write(DeckCardRef card) => ArenaDeckListWriter.Write(Deck([card]));

    private static DeckCardRef Card(string name, int quantity) => new(name, quantity, DeckBoard.Main);

    private static CandidateDeck Deck(IReadOnlyList<DeckCardRef> cards) => new(
        SourceId: "test:deck",
        Name: "Test",
        Url: "",
        FormatKey: Formats.Standard.Key,
        Popularity: 0,
        Cards: cards,
        FetchedAt: DateTimeOffset.UtcNow);
}
