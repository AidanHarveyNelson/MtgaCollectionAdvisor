using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Analysis;

public sealed class DeckRankingService(WildcardCalculator calculator)
{
    /// <summary>
    /// Ranks candidate decks ascending by total wildcards needed to complete them from
    /// the given collection - the cheapest-to-finish deck first, regardless of how
    /// strong or popular it is. Ties break by how much of the deck is already owned.
    /// </summary>
    public async Task<IReadOnlyList<DeckAnalysisResult>> RankAsync(
        FormatDefinition format,
        IReadOnlyList<CandidateDeck> decks,
        CollectionSnapshot collection,
        CancellationToken ct = default)
    {
        var nameCache = new Dictionary<string, IReadOnlyList<CardInfo>>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DeckAnalysisResult>(decks.Count);

        foreach (var deck in decks)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await calculator.AnalyzeAsync(deck, collection, format, nameCache, ct));
        }

        return results
            .OrderBy(r => r.Needed.Total)
            .ThenByDescending(r => r.OwnedFraction)
            .ToList();
    }
}
