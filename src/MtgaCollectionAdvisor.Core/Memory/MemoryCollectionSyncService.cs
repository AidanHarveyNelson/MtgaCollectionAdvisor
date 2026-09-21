using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Memory;

public sealed record MemorySyncResult(int DistinctCards, int TotalCopies, double KnownRatio, string AnchorUsed);

/// <summary>
/// Captures the collection straight from the running MTGA client's memory and stores it,
/// which is the only way to read it on current client builds - the collection is no
/// longer written to Player.log, nor cached in any local file.
/// </summary>
public sealed class MemoryCollectionSyncService(
    CardDatabaseStore cardDatabaseStore,
    CollectionStore collectionStore)
{
    private MemoryRegion? _lastKnownRegion;

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>
    /// Resolves an anchor card by name against the local card database. A name can map to
    /// several Arena printings and we cannot know which one the player owns, so every
    /// printing becomes its own anchor candidate.
    /// </summary>
    public async Task<IReadOnlyList<CollectionAnchor>> BuildAnchorsAsync(
        string cardName, int quantity, CancellationToken ct = default)
    {
        var printings = await cardDatabaseStore.FindByNameAsync(cardName, ct);
        return printings
            .Select(p => new CollectionAnchor(p.GrpId, quantity, $"{p.Name} ({p.SetCode})"))
            .ToList();
    }

    /// <summary>
    /// Scans with no input from the player at all. Remembers where the collection was
    /// found so a later refresh can go straight back to that region.
    /// </summary>
    public Task<MemorySyncResult?> SyncAutomaticallyAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
        => RunAsync((scanner, knownIds) => scanner.ScanWithoutAnchors(knownIds, _lastKnownRegion, progress, ct), ct);

    public static bool IsMtgaRunning() =>
        System.Diagnostics.Process.GetProcessesByName(CollectionMemoryScanner.MtgaProcessName).Length > 0;

    public Task<MemorySyncResult?> SyncAsync(
        IReadOnlyCollection<CollectionAnchor> anchors,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => RunAsync((scanner, knownIds) => scanner.Scan(anchors, knownIds, progress, ct), ct);

    private async Task<MemorySyncResult?> RunAsync(
        Func<CollectionMemoryScanner, IReadOnlySet<int>, MemoryScanResult?> scan, CancellationToken ct)
    {
        var knownIds = await cardDatabaseStore.GetAllGrpIdsAsync(ct);
        if (knownIds.Count == 0)
        {
            throw new MemoryScanException(
                "The card database is empty. Click \"Update cards\" before scanning.");
        }

        var scanner = new CollectionMemoryScanner();
        var result = await Task.Run(() => scan(scanner, knownIds), ct);
        if (result is null) return null;

        await collectionStore.SaveCollectionAsync(result.Collection, ct);
        _lastKnownRegion = result.FoundInRegion;
        LastSyncedAt = DateTimeOffset.Now;

        return new MemorySyncResult(
            DistinctCards: result.Collection.Count,
            TotalCopies: result.Collection.Values.Sum(),
            KnownRatio: result.KnownRatio,
            AnchorUsed: result.MatchedAnchor.Name);
    }
}
