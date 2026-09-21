using System.Text.Json.Serialization;

namespace MtgaCollectionAdvisor.Core.Cards;

internal sealed class ScryfallBulkDataEntry
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>
    /// Scryfall retired the old single-JSON-array "download_uri" field; bulk data is
    /// now published as gzip-compressed JSON Lines (one card object per line).
    /// </summary>
    [JsonPropertyName("jsonl_download_uri")] public string JsonlDownloadUri { get; set; } = "";
}

internal sealed class ScryfallBulkDataResponse
{
    [JsonPropertyName("data")] public List<ScryfallBulkDataEntry> Data { get; set; } = [];
}

internal sealed class ScryfallCardFace
{
    [JsonPropertyName("mana_cost")] public string? ManaCost { get; set; }
    [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
}

internal sealed class ScryfallCard
{
    [JsonPropertyName("arena_id")] public int? ArenaId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("set")] public string Set { get; set; } = "";
    [JsonPropertyName("type_line")] public string TypeLine { get; set; } = "";
    [JsonPropertyName("mana_cost")] public string? ManaCost { get; set; }
    [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
    [JsonPropertyName("rarity")] public string Rarity { get; set; } = "";
    [JsonPropertyName("legalities")] public Dictionary<string, string> Legalities { get; set; } = [];
    [JsonPropertyName("card_faces")] public List<ScryfallCardFace>? CardFaces { get; set; }

    public string EffectiveManaCost =>
        !string.IsNullOrEmpty(ManaCost) ? ManaCost :
        CardFaces?.FirstOrDefault()?.ManaCost ?? "";

    public string EffectiveColors()
    {
        if (Colors is { Count: > 0 }) return string.Concat(Colors);
        if (CardFaces is null) return "";
        var union = CardFaces
            .SelectMany(f => f.Colors ?? [])
            .Distinct()
            .OrderBy(c => c);
        return string.Concat(union);
    }

    public bool IsLegal(string formatKey) =>
        Legalities.TryGetValue(formatKey, out var status) && status == "legal";
}
