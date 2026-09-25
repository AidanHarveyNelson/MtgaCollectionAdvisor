namespace MtgaCollectionAdvisor.Core.Models;

public enum CardRarity
{
    Unknown = 0,
    Basic,
    Common,
    Uncommon,
    Rare,
    Mythic
}

/// <summary>
/// Card metadata sourced from Scryfall's bulk data, keyed by the Arena "grpId"
/// (Scryfall's arena_id field), which is the same identifier used in the MTGA
/// collection dump and wildcard economy. The image URLs are Scryfall's "normal" size, on its
/// image CDN; the back face's is only set for a double-faced card.
/// </summary>
public sealed record CardInfo(
    int GrpId,
    string Name,
    string SetCode,
    string ManaCost,
    string Colors,
    CardRarity Rarity,
    bool StandardLegal,
    bool PioneerLegal,
    string? ImageUrl = null,
    string? BackImageUrl = null,
    bool? IsNonBasicLand = null);
