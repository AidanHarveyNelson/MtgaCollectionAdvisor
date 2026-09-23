using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Export;

/// <summary>A card name the user owns, with every Arena id they own it under.</summary>
public sealed record ExportedCard(string Name, int Count, IReadOnlyList<int> ArenaIds);

/// <summary>
/// Writes the collection out in the two shapes the app already reads back in: an Arena
/// <c>&lt;qty&gt; &lt;name&gt;</c> list (<see cref="Cards.CollectionListParser"/>) and the
/// <c>mtga_collection.json</c> shape of MTGA-collection-exporter
/// (<see cref="Cards.CollectionExporterJsonImporter"/>). Everything is local-only; the point
/// is that the user can always walk away with their data.
/// </summary>
public static class CollectionExportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// One entry per card name, owned copies summed across printings. An id missing from
    /// the card database keeps an empty name: it cannot be written as text, but the JSON
    /// still carries it by id.
    /// </summary>
    public static IReadOnlyList<ExportedCard> Group(CollectionSnapshot collection, IReadOnlyDictionary<int, string> names) =>
        collection.OwnedByGrpId
            .Where(kv => kv.Value > 0)
            .GroupBy(kv => names.TryGetValue(kv.Key, out var name) ? name : "", StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Key.Length == 0
                // Unknown ids stay separate: summing them under one empty name would lose which is which.
                ? g.Select(kv => new ExportedCard("", kv.Value, [kv.Key]))
                : [new ExportedCard(g.Key, g.Sum(kv => kv.Value), g.Select(kv => kv.Key).Order().ToList())])
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ArenaIds[0])
            .ToList();

    /// <summary>
    /// Arena's list format, front face only, the same way a deck is exported
    /// (<see cref="ArenaDeckListWriter.ArenaName"/>). Cards with no known name are left out.
    /// </summary>
    public static string WriteText(IReadOnlyList<ExportedCard> cards)
    {
        var sb = new StringBuilder();
        foreach (var card in cards.Where(c => c.Name.Length > 0))
        {
            sb.AppendLine($"{card.Count} {ArenaDeckListWriter.ArenaName(card.Name)}");
        }
        return sb.ToString();
    }

    public static string WriteJson(IReadOnlyList<ExportedCard> cards, WildcardInventory wildcards, DateTimeOffset exportedAt) =>
        JsonSerializer.Serialize(new CollectionFile
        {
            ExportedAt = exportedAt,
            Wildcards = new WildcardsEntry
            {
                Common = wildcards.Commons,
                Uncommon = wildcards.Uncommons,
                Rare = wildcards.Rares,
                Mythic = wildcards.Mythics,
            },
            Cards = cards.Select(c => new CardEntry { Count = c.Count, Name = c.Name, ArenaIds = c.ArenaIds }).ToList(),
        }, JsonOptions);

    // Property names follow mtga_collection.json, so the file reads back through the same
    // importer as the exporter tool's own output. The extra keys are ignored on import.
    private sealed class CollectionFile
    {
        [JsonPropertyName("exported_at")] public DateTimeOffset ExportedAt { get; init; }
        [JsonPropertyName("wildcards")] public WildcardsEntry Wildcards { get; init; } = new();
        [JsonPropertyName("cards")] public List<CardEntry> Cards { get; init; } = [];
    }

    private sealed class WildcardsEntry
    {
        [JsonPropertyName("common")] public int Common { get; init; }
        [JsonPropertyName("uncommon")] public int Uncommon { get; init; }
        [JsonPropertyName("rare")] public int Rare { get; init; }
        [JsonPropertyName("mythic")] public int Mythic { get; init; }
    }

    private sealed class CardEntry
    {
        [JsonPropertyName("count")] public int Count { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("arena_ids")] public IReadOnlyList<int> ArenaIds { get; init; } = [];
    }
}
