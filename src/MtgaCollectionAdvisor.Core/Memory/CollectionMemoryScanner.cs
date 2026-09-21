using System.Buffers.Binary;

namespace MtgaCollectionAdvisor.Core.Memory;

/// <summary>
/// A card the user knows they own, with the exact number of copies. The scanner looks
/// for the byte pattern of that (grpId, quantity) pair to locate the collection table.
/// </summary>
public sealed record CollectionAnchor(int GrpId, int Quantity, string Name);

public sealed record MemoryScanResult(
    IReadOnlyDictionary<int, int> Collection,
    int Duplicates,
    double KnownRatio,
    CollectionAnchor MatchedAnchor,
    MemoryRegion? FoundInRegion = null);

/// <summary>
/// Locates the player's collection inside the running MTGA client's memory.
///
/// The client keeps the collection as a flat table of (grpId, quantity) 32-bit pairs.
/// Given a card the player knows they own ("4 copies of X"), the scanner searches for
/// that exact 8-byte pattern, then walks outward decoding consecutive pairs into a
/// candidate block. Several blocks usually match, so each is scored on how many of its
/// ids are real Arena cards, how many anchors it contains, its size, and how few
/// duplicate ids it has - the best scoring block wins, if it passes validation.
///
/// Algorithm adapted from the MIT-licensed MTGA-collection-exporter
/// (github.com/NthPhantom10/MTGA-collection-exporter).
/// </summary>
public sealed class CollectionMemoryScanner
{
    public const string MtgaProcessName = "MTGA";

    private const int MinArenaId = 1_000;
    private const int MaxArenaId = 900_000;
    private const int MinQuantity = 1;
    private const int MaxQuantity = 400;
    private const int MinBlockSize = 50;
    private const int MaxGap = 64;
    private const int ScanWindowBytes = 8 * 1024 * 1024;
    private const int ChunkBytes = 4 * 1024 * 1024;

    /// <summary>Room for ~64k (grpId, quantity) entries, far more than any collection.</summary>
    private const int ChunkOverlapBytes = 1024 * 1024;
    private const long MinAddressGap = 1024 * 1024;

    private static readonly int[] StridesInWords = [2, 3, 4];

    /// <summary>
    /// Anchorless scan: sweeps every writable region looking for dense tables of
    /// (grpId, quantity) pairs, then keeps the block that looks most like a real
    /// collection - mostly known Arena ids, with playset-sized quantities. Needs no
    /// input from the player, at the cost of scanning far more memory.
    /// </summary>
    public MemoryScanResult? ScanWithoutAnchors(
        IReadOnlySet<int> knownArenaIds,
        MemoryRegion? hintRegion = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        using var reader = ProcessMemoryReader.Open(MtgaProcessName);
        var regions = reader.EnumerateWritableRegions().ToList();

        // A refresh usually finds the table still living in the region it was in last
        // time, which turns a multi-minute sweep into a sub-second read.
        if (hintRegion is not null)
        {
            var stillMapped = regions.FirstOrDefault(r =>
                r.BaseAddress == hintRegion.BaseAddress && r.Size == hintRegion.Size);
            if (stillMapped is not null)
            {
                progress?.Report("Trying the region the collection was in last time...");
                var quick = SweepRegions(reader, [stillMapped], [2, 3, 4], knownArenaIds, progress, ct);
                var quickBest = RankCollectionCandidates(quick, knownArenaIds).FirstOrDefault();
                if (quickBest is not null)
                {
                    progress?.Report($"Collection found again quickly: {quickBest.Block.Count} distinct cards.");
                    return new MemoryScanResult(quickBest.Block, quickBest.Duplicates, quickBest.KnownRatio,
                        new CollectionAnchor(0, 0, "automatic sweep"), stillMapped);
                }
                progress?.Report("That region no longer holds it; sweeping everything.");
            }
        }

        var totalBytes = regions.Sum(r => r.Size);
        progress?.Report($"{regions.Count} regions, {totalBytes / (1024 * 1024)} MB to sweep.");

        // Each chunk is decoded with every stride/offset in one pass: stride 2 is a packed
        // pair array, stride 4 offset 2 is a .NET Dictionary<int,int> entry table
        // ({hash, next, key, value}), stride 3 covers 12-byte structs.
        var allCandidates = SweepRegions(reader, regions, [2, 3, 4], knownArenaIds, progress, ct);
        progress?.Report($"{allCandidates.Count} candidate blocks.");

        var best = RankCollectionCandidates(allCandidates, knownArenaIds).FirstOrDefault();
        if (best is null) return null;

        var bestRegion = allCandidates.FirstOrDefault(c => ReferenceEquals(c.Block, best.Block))?.Region;

        progress?.Report(
            $"Collection found: {best.Block.Count} distinct cards " +
            $"({best.KnownRatio:P0} known ids, {best.MultiCopyRatio:P0} with 2+ copies).");

        return new MemoryScanResult(
            best.Block, best.Duplicates, best.KnownRatio,
            new CollectionAnchor(0, 0, "automatic sweep"), bestRegion);
    }

    public sealed record ScoredBlock(
        Dictionary<int, int> Block, int Duplicates, double Score, double KnownRatio, double MultiCopyRatio,
        double AverageCopies);

    /// <summary>
    /// Scores candidate blocks on how much they look like a real Arena collection, not
    /// just any table of ints: nearly all ids must be real cards, and a real collection
    /// has a healthy share of 2-4 copy entries. A block where every quantity is exactly 1
    /// is almost always a UI/catalogue list rather than the collection.
    /// </summary>
    public static IReadOnlyList<ScoredBlock> RankCollectionCandidates(
        List<CandidateBlock> candidates, IReadOnlySet<int> knownArenaIds)
    {
        var scored = candidates
            .Where(c => c.Block.Count >= 10)
            .Select(c =>
            {
                var knownRatio = (double)c.Block.Keys.Count(knownArenaIds.Contains) / c.Block.Count;
                var multiCopyRatio = (double)c.Block.Values.Count(v => v >= 2) / c.Block.Count;
                var playsetRatio = (double)c.Block.Values.Count(v => v <= 4) / c.Block.Count;
                var averageCopies = (double)c.Block.Values.Sum() / c.Block.Count;
                var sizeScore = Math.Min(c.Block.Count / 2000.0, 1.0);
                var duplicateRatio = (double)c.Duplicates / Math.Max(1, c.Block.Count + c.Duplicates);

                var score = knownRatio * 0.25
                            + sizeScore * 0.25
                            + Math.Min(multiCopyRatio / 0.30, 1.0) * 0.25
                            + playsetRatio * 0.15
                            + (1.0 - duplicateRatio) * 0.10;

                return new ScoredBlock(c.Block, c.Duplicates, score, knownRatio, multiCopyRatio, averageCopies);
            })
            // A real collection is almost entirely real cards, holds a lot of 2-4 ofs, and
            // cannot average more than 4 copies per card (basic lands aside).
            .Where(s => s.KnownRatio >= 0.80 && s.MultiCopyRatio > 0.05 && s.AverageCopies <= 5.0)
            .OrderByDescending(s => s.Block.Count)
            .ThenByDescending(s => s.Score)
            .ToList();

        return DropSubsets(scored);
    }

    /// <summary>
    /// The client keeps partial views of the collection in memory too (a filtered page,
    /// a format-restricted pool), and those score just as well as the real thing. Since
    /// the collection is the maximal such table, any block whose cards are essentially
    /// contained in a bigger one is a view of it, not the collection itself.
    /// </summary>
    private static List<ScoredBlock> DropSubsets(List<ScoredBlock> scored)
    {
        const double containmentThreshold = 0.90;
        var kept = new List<ScoredBlock>();

        foreach (var candidate in scored) // already ordered from largest to smallest
        {
            var isViewOfKept = kept.Any(bigger =>
            {
                var shared = candidate.Block.Keys.Count(bigger.Block.ContainsKey);
                return (double)shared / candidate.Block.Count >= containmentThreshold;
            });

            if (!isViewOfKept) kept.Add(candidate);
        }

        return kept.OrderByDescending(s => s.Score).ToList();
    }

    public sealed record CandidateBlock(Dictionary<int, int> Block, int Duplicates, MemoryRegion Region);

    /// <summary>Diagnostic entry point: every candidate block found, unscored.</summary>
    public List<CandidateBlock> CollectCandidates(
        IReadOnlySet<int> knownArenaIds, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var reader = ProcessMemoryReader.Open(MtgaProcessName);
        var regions = reader.EnumerateWritableRegions().ToList();
        return SweepRegions(reader, regions, [2, 3, 4], knownArenaIds, progress, ct);
    }

    private static List<CandidateBlock> SweepRegions(
        ProcessMemoryReader reader,
        IReadOnlyList<MemoryRegion> regions,
        int[] strides,
        IReadOnlySet<int> knownArenaIds,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var candidates = new List<CandidateBlock>();
        var buffer = new byte[ChunkBytes];
        var scanned = 0;

        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            if (scanned % 400 == 0)
            {
                progress?.Report($"Sweeping... ({scanned}/{regions.Count} regions, {candidates.Count} blocks)");
            }

            // Chunks overlap so a collection table sitting across a chunk boundary is
            // still seen whole by at least one read - without it the table gets split
            // and only the larger half survives.
            for (long offset = 0; offset < region.Size; offset += ChunkBytes - ChunkOverlapBytes)
            {
                var toRead = (int)Math.Min(ChunkBytes, region.Size - offset);
                if (toRead < MinBlockSize * 8) break;

                var read = reader.ReadPartial(region.BaseAddress + offset, buffer, toRead);
                if (read < MinBlockSize * 8) break;

                var words = read / 4;
                foreach (var stride in strides)
                {
                    for (var wordOffset = 0; wordOffset < stride; wordOffset++)
                    {
                        foreach (var block in ExtractBlocks(buffer.AsSpan(0, read), words, stride, wordOffset))
                        {
                            // Cheap pre-filter: a real collection is mostly cards we know.
                            var known = block.Block.Keys.Count(knownArenaIds.Contains);
                            if ((double)known / block.Block.Count >= 0.5)
                            {
                                candidates.Add(new CandidateBlock(block.Block, block.Duplicates, region));
                            }
                        }
                    }
                }
            }
        }

        return candidates;
    }

    public MemoryScanResult? Scan(
        IReadOnlyCollection<CollectionAnchor> anchors,
        IReadOnlySet<int> knownArenaIds,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (anchors.Count == 0) throw new MemoryScanException("Provide at least one anchor card.");

        using var reader = ProcessMemoryReader.Open(MtgaProcessName);
        var regions = reader.EnumerateWritableRegions().ToList();
        progress?.Report($"{regions.Count} memory regions to sweep.");

        foreach (var anchor in anchors.OrderByDescending(a => a.Quantity))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Looking for anchor: {anchor.Name} x{anchor.Quantity}...");

            var hits = FindPattern(reader, regions, anchor, progress, ct);
            if (hits.Count == 0)
            {
                progress?.Report($"No match for {anchor.Name} x{anchor.Quantity}.");
                continue;
            }

            progress?.Report($"{hits.Count} match(es). Extracting blocks...");

            var candidates = new List<(Dictionary<int, int> Block, int Duplicates)>();
            foreach (var hit in hits)
            {
                ct.ThrowIfCancellationRequested();
                candidates.AddRange(ExtractBlocksAround(reader, hit));
            }

            if (candidates.Count == 0)
            {
                progress?.Report("No valid data block near the matches.");
                continue;
            }

            progress?.Report($"Scoring {candidates.Count} candidate blocks...");
            var best = SelectBest(candidates, anchors, knownArenaIds);
            if (best is null) continue;

            var (block, duplicates) = best.Value;
            if (!IsValid(block, duplicates, knownArenaIds, out var knownRatio))
            {
                progress?.Report("Best block failed validation. Trying the next anchor...");
                continue;
            }

            // The anchor quantities are ground truth supplied by the user.
            foreach (var a in anchors)
            {
                if (block.ContainsKey(a.GrpId)) block[a.GrpId] = a.Quantity;
            }

            progress?.Report($"Collection found: {block.Count} distinct cards.");
            return new MemoryScanResult(block, duplicates, knownRatio, anchor);
        }

        return null;
    }

    private static List<MemoryHit> FindPattern(
        ProcessMemoryReader reader,
        IReadOnlyList<MemoryRegion> regions,
        CollectionAnchor anchor,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        Span<byte> pattern = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(pattern[..4], anchor.GrpId);
        BinaryPrimitives.WriteInt32LittleEndian(pattern[4..], anchor.Quantity);
        var patternArray = pattern.ToArray();

        var hits = new List<MemoryHit>();
        var buffer = new byte[ChunkBytes];
        long lastKept = long.MinValue;
        var scannedRegions = 0;

        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();
            scannedRegions++;
            if (scannedRegions % 500 == 0)
            {
                progress?.Report($"Sweeping memory... ({scannedRegions}/{regions.Count} regions, {hits.Count} matches)");
            }

            for (long offset = 0; offset < region.Size; offset += ChunkBytes - patternArray.Length)
            {
                var toRead = (int)Math.Min(ChunkBytes, region.Size - offset);
                if (toRead < patternArray.Length) break;

                var read = reader.ReadPartial(region.BaseAddress + offset, buffer, toRead);
                if (read < patternArray.Length) break;

                var searchSpan = buffer.AsSpan(0, read);
                var searchStart = 0;
                while (true)
                {
                    var index = searchSpan[searchStart..].IndexOf(patternArray);
                    if (index < 0) break;

                    var absolute = region.BaseAddress + offset + searchStart + index;
                    if (absolute - lastKept > MinAddressGap)
                    {
                        hits.Add(new MemoryHit(absolute, region));
                        lastKept = absolute;
                    }

                    searchStart += index + 1;
                    if (searchStart >= searchSpan.Length) break;
                }
            }
        }

        return hits;
    }

    private static IEnumerable<(Dictionary<int, int> Block, int Duplicates)> ExtractBlocksAround(
        ProcessMemoryReader reader, MemoryHit hit)
    {
        var regionEnd = hit.Region.BaseAddress + hit.Region.Size;
        var start = Math.Max(hit.Region.BaseAddress, hit.Address - ScanWindowBytes / 2);
        var length = (int)Math.Min(ScanWindowBytes, regionEnd - start);
        if (length < 64) return [];

        var buffer = new byte[length];
        var read = reader.ReadPartial(start, buffer, length);
        if (read < 64) return [];

        var results = new List<(Dictionary<int, int>, int)>();
        var words = read / 4;
        foreach (var stride in StridesInWords)
        {
            for (var wordOffset = 0; wordOffset < stride; wordOffset++)
            {
                results.AddRange(ExtractBlocks(buffer.AsSpan(0, read), words, stride, wordOffset));
            }
        }
        return results;
    }

    private static List<(Dictionary<int, int> Block, int Duplicates)> ExtractBlocks(
        ReadOnlySpan<byte> data, int words, int strideInWords, int wordOffset)
    {
        var blocks = new List<(Dictionary<int, int>, int)>();
        var current = new Dictionary<int, int>();
        var duplicates = 0;
        var misses = 0;

        for (var i = wordOffset; i + 1 < words; i += strideInWords)
        {
            var key = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i * 4, 4));
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((i + 1) * 4, 4));

            if (key is >= MinArenaId and < MaxArenaId && value is >= MinQuantity and <= MaxQuantity)
            {
                if (!current.TryAdd(key, value)) duplicates++;
                misses = 0;
            }
            else
            {
                misses++;
                if (misses > MaxGap)
                {
                    if (current.Count >= MinBlockSize) blocks.Add((current, duplicates));
                    current = new Dictionary<int, int>();
                    duplicates = 0;
                    misses = 0;
                }
            }
        }

        if (current.Count >= MinBlockSize) blocks.Add((current, duplicates));
        return blocks;
    }

    private static (Dictionary<int, int> Block, int Duplicates)? SelectBest(
        List<(Dictionary<int, int> Block, int Duplicates)> candidates,
        IReadOnlyCollection<CollectionAnchor> anchors,
        IReadOnlySet<int> knownArenaIds)
    {
        var anchorPairs = anchors.Select(a => (a.GrpId, a.Quantity)).ToHashSet();
        var anchorIds = anchors.Select(a => a.GrpId).ToHashSet();
        var anchorCount = Math.Max(1, anchors.Count);

        return candidates
            .Where(c => c.Block.Count > 0)
            .Select(c =>
            {
                var known = c.Block.Keys.Count(knownArenaIds.Contains);
                var knownRatio = (double)known / c.Block.Count;
                var anchorsExact = c.Block.Count(kv => anchorPairs.Contains((kv.Key, kv.Value)));
                var anchorsById = c.Block.Keys.Count(anchorIds.Contains);
                var sizeScore = Math.Min(c.Block.Count / 5000.0, 1.0);
                var duplicateRatio = (double)c.Duplicates / Math.Max(1, c.Block.Count + c.Duplicates);

                var score = knownRatio * 0.35
                            + (double)anchorsExact / anchorCount * 0.35
                            + (double)anchorsById / anchorCount * 0.10
                            + sizeScore * 0.10
                            + (1.0 - duplicateRatio) * 0.10;

                return (c.Block, c.Duplicates, Score: score, AnchorsExact: anchorsExact, Known: known);
            })
            .OrderByDescending(x => x.Duplicates == 0)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.AnchorsExact)
            .ThenByDescending(x => x.Known)
            .ThenByDescending(x => x.Block.Count)
            .Select(x => ((Dictionary<int, int>, int)?)(x.Block, x.Duplicates))
            .FirstOrDefault();
    }

    private static bool IsValid(
        Dictionary<int, int> block, int duplicates, IReadOnlySet<int> knownArenaIds, out double knownRatio)
    {
        knownRatio = 0;
        if (block.Count is < 10 or > 100_000) return false;

        knownRatio = (double)block.Keys.Count(knownArenaIds.Contains) / block.Count;
        if (knownRatio < 0.30) return false;

        if (block.Values.Sum() > 500_000) return false;
        if (duplicates > Math.Max(25, (int)(block.Count * 0.05))) return false;

        return true;
    }

    private readonly record struct MemoryHit(long Address, MemoryRegion Region);
}
