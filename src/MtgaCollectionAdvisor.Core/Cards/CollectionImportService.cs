using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Cards;

public sealed record CollectionImportResult(int MatchedNames, int UnmatchedCount, IReadOnlyList<string> UnmatchedNames);

/// <summary>
/// Resolves a hand-pasted collection list (see <see cref="CollectionListParser"/>) to
/// Arena grpIds via the local card database and replaces the stored collection with it.
/// A name can have several printings; since <c>WildcardCalculator</c> already sums
/// owned copies across every printing of a name, it does not matter which one of them
/// receives the imported quantity.
/// </summary>
public sealed class CollectionImportService(CardDatabaseStore cardStore, CollectionStore collectionStore)
{
    public async Task<CollectionImportResult> ImportAsync(string text, CancellationToken ct = default)
    {
        var parsed = CollectionListParser.Parse(text);
        var ownedByGrpId = new Dictionary<int, int>();
        var unmatched = new List<string>();

        foreach (var (name, qty) in parsed)
        {
            var printings = await cardStore.FindByNameAsync(name, ct);
            if (printings.Count == 0)
            {
                unmatched.Add(name);
                continue;
            }

            var grpId = printings[0].GrpId;
            ownedByGrpId[grpId] = ownedByGrpId.TryGetValue(grpId, out var existing) ? existing + qty : qty;
        }

        await collectionStore.SaveCollectionAsync(ownedByGrpId, ct);

        return new CollectionImportResult(parsed.Count - unmatched.Count, unmatched.Count, unmatched);
    }
}
