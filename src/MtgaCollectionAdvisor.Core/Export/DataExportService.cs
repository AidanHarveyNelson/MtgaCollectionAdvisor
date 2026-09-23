using System.Text;
using MtgaCollectionAdvisor.Core.Arena;
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
public sealed class DataExportService(
    CollectionStore collection, CardDatabaseStore cards, CuratedDeckStore decks, ArenaDeckStore arenaDecks)
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

    /// <summary>
    /// The decks saved in Arena, as last logged at login. Wizards' decks (precons and
    /// Arena's suggested decks) only when asked for. Null when Arena has never been seen
    /// logging its decks.
    /// </summary>
    public async Task<ExportFile?> ArenaDecksAsync(bool includeWizards, CancellationToken ct = default)
    {
        if (await arenaDecks.LoadAsync(ct) is not { } snapshot) return null;

        var names = await cards.GetNamesAsync(ct);
        var files = snapshot.Decks
            .Where(d => includeWizards || !d.IsWizardsDeck)
            .Select(d => new ZipEntryFile(ArenaDeckTextWriter.FolderFor(d.Format), d.Name, ArenaDeckTextWriter.Write(d, names)));

        var suffix = includeWizards ? "-all" : "";
        return new ExportFile($"mtga-arena-decks{suffix}-{Today}.zip", "application/zip", UserDeckExportWriter.WriteZip(files));
    }

    private async Task<(IReadOnlyList<ExportedCard> Grouped, CollectionSnapshot Snapshot)> LoadCollectionAsync(CancellationToken ct)
    {
        var snapshot = await collection.LoadAsync(ct);
        var names = await cards.GetNamesAsync(ct);
        return (CollectionExportWriter.Group(snapshot, names), snapshot);
    }

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");
}
