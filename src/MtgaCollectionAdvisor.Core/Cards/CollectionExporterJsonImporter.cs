using System.Text.Json.Serialization;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Cards;

internal sealed class ExporterCollectionFile
{
    [JsonPropertyName("cards")] public List<ExporterCardEntry> Cards { get; set; } = [];
}

internal sealed class ExporterCardEntry
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arena_ids")] public List<int> ArenaIds { get; set; } = [];
}

/// <summary>
/// Imports the "mtga_collection.json" file produced by the community
/// MTGA-collection-exporter tool (github.com/NthPhantom10/MTGA-collection-exporter),
/// which reads the player's collection directly from MTGA's process memory. Each entry
/// already carries the exact Arena grpIds it represents, so unlike the pasted-text
/// import there is no name-matching ambiguity - the whole count is deposited on the
/// first grpId listed (WildcardCalculator sums owned copies across every printing of a
/// name anyway, so it does not matter which one).
/// </summary>
public sealed class CollectionExporterJsonImporter(CollectionStore collectionStore)
{
    public async Task<int> ImportAsync(string json, CancellationToken ct = default)
    {
        var file = System.Text.Json.JsonSerializer.Deserialize<ExporterCollectionFile>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Arquivo de coleção inválido ou vazio.");

        var ownedByGrpId = new Dictionary<int, int>();
        foreach (var entry in file.Cards)
        {
            if (entry.ArenaIds.Count == 0 || entry.Count <= 0) continue;
            var grpId = entry.ArenaIds[0];
            ownedByGrpId[grpId] = ownedByGrpId.TryGetValue(grpId, out var existing) ? existing + entry.Count : entry.Count;
        }

        await collectionStore.SaveCollectionAsync(ownedByGrpId, ct);
        return ownedByGrpId.Count;
    }
}
