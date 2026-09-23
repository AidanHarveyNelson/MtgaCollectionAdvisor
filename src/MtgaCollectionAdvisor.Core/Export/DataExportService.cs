using System.Text;
using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Export;

/// <summary>A file ready to hand to the browser as a download.</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Gathers what the user owns and has built and writes it out. Reads only: nothing here
/// touches the database beyond loading.
/// </summary>
public sealed class DataExportService(CollectionStore collection, CardDatabaseStore cards, CuratedDeckStore decks)
{
    public async Task<ExportFile> CollectionTextAsync(CancellationToken ct = default)
    {
        var (grouped, _) = await LoadCollectionAsync(ct);
        return new ExportFile(
            $"mtga-collection-{Today}.txt", "text/plain; charset=utf-8",
            Encoding.UTF8.GetBytes(CollectionExportWriter.WriteText(grouped)));
    }

    public async Task<ExportFile> CollectionJsonAsync(CancellationToken ct = default)
    {
        var (grouped, snapshot) = await LoadCollectionAsync(ct);
        return new ExportFile(
            $"mtga-collection-{Today}.json", "application/json",
            Encoding.UTF8.GetBytes(CollectionExportWriter.WriteJson(grouped, snapshot.Wildcards, DateTimeOffset.UtcNow)));
    }

    /// <summary>The user's own decks in every format; fetched decks are not theirs to take.</summary>
    public async Task<ExportFile> UserDecksAsync(CancellationToken ct = default)
    {
        var userDecks = new List<CandidateDeck>();
        foreach (var format in Formats.All)
        {
            userDecks.AddRange((await decks.LoadAsync(format, ct)).Where(d => d.IsUserDeck));
        }

        return new ExportFile($"mtga-user-decks-{Today}.zip", "application/zip", UserDeckExportWriter.WriteZip(userDecks));
    }

    private async Task<(IReadOnlyList<ExportedCard> Grouped, CollectionSnapshot Snapshot)> LoadCollectionAsync(CancellationToken ct)
    {
        var snapshot = await collection.LoadAsync(ct);
        var names = await cards.GetNamesAsync(ct);
        return (CollectionExportWriter.Group(snapshot, names), snapshot);
    }

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");
}
