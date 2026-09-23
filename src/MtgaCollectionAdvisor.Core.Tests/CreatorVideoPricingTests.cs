using MtgaCollectionAdvisor.Core.Creators;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class CreatorVideoPricingTests
{
    [Fact]
    public void PickBest_Should_PreferTheLegalFormat()
    {
        var (format, _) = CreatorVideoPricing.PickBest(
        [
            (Formats.Standard, Analysis(illegal: 3)),
            (Formats.Pioneer, Analysis(illegal: 0))
        ]);

        Assert.Equal(Formats.Pioneer.Key, format.Key);
    }

    [Fact]
    public void PickBest_Should_PickFewestIllegal_When_LegalInNone()
    {
        var (fewest, _) = CreatorVideoPricing.PickBest(
        [
            (Formats.Standard, Analysis(illegal: 5)),
            (Formats.Pioneer, Analysis(illegal: 2))
        ]);
        var (tie, _) = CreatorVideoPricing.PickBest(
        [
            (Formats.Pioneer, Analysis(illegal: 2)),
            (Formats.Standard, Analysis(illegal: 2))
        ]);

        Assert.Equal(Formats.Pioneer.Key, fewest.Key);
        Assert.Equal(Formats.Standard.Key, tie.Key); // ties follow Formats.All, not input order
    }

    internal static DeckAnalysisResult Analysis(int illegal = 0, int rares = 0, int ownedCopies = 0) =>
        new(
            Deck: new CandidateDeck("video:x", "Deck", "", Formats.Standard.Key, 0,
                [new DeckCardRef("Card", 4, DeckBoard.Main)], DateTimeOffset.UtcNow),
            Needed: new WildcardNeed(0, 0, rares, 0),
            OwnedCopies: ownedCopies,
            TotalCopies: 4,
            Gaps: [],
            UnavailableOnArena: [],
            IllegalInFormat: Enumerable.Range(0, illegal).Select(i => $"Illegal {i}").ToList(),
            Colors: "R");
}
