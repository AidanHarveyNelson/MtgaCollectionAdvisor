using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class ArenaDeckListParserTests
{
    // Arena's own export: the companion has its own section and is repeated in the
    // sideboard, where it actually lives.
    private const string ExportWithCompanion = """
        Companion
        1 Lurrus of the Dream-Den

        Deck
        4 Fatal Push
        4 Swamp

        Sideboard
        1 Lurrus of the Dream-Den
        2 Duress
        """;

    [Fact]
    public void Parse_Should_SkipCompanionSection()
    {
        var cards = ArenaDeckListParser.Parse(ExportWithCompanion);

        Assert.DoesNotContain(cards, c => c.Name == "Lurrus of the Dream-Den" && c.Board == DeckBoard.Main);
        var lurrus = Assert.Single(cards, c => c.Name == "Lurrus of the Dream-Den");
        Assert.Equal(DeckBoard.Sideboard, lurrus.Board);
        Assert.Equal(8, cards.Where(c => c.Board == DeckBoard.Main).Sum(c => c.Quantity));
    }

    [Fact]
    public void Parse_Should_ResumeMainboard_After_CompanionSection()
    {
        var cards = ArenaDeckListParser.Parse(ExportWithCompanion);

        Assert.Contains(cards, c => c.Name == "Fatal Push" && c.Board == DeckBoard.Main && c.Quantity == 4);
        Assert.Contains(cards, c => c.Name == "Duress" && c.Board == DeckBoard.Sideboard);
    }
}
