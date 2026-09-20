using System.Text.Json.Serialization;

namespace MtgaCollectionAdvisor.Core.Decks;

internal sealed class MoxfieldSearchResponse
{
    [JsonPropertyName("data")] public List<MoxfieldSearchResult> Data { get; set; } = [];
}

internal sealed class MoxfieldSearchResult
{
    [JsonPropertyName("publicId")] public string PublicId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("likes")] public int Likes { get; set; }
    [JsonPropertyName("views")] public int Views { get; set; }
}

internal sealed class MoxfieldBoardCard
{
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

internal sealed class MoxfieldDeckDetail
{
    [JsonPropertyName("publicId")] public string PublicId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mainboard")] public Dictionary<string, MoxfieldBoardCard> Mainboard { get; set; } = [];
    [JsonPropertyName("sideboard")] public Dictionary<string, MoxfieldBoardCard> Sideboard { get; set; } = [];
}
