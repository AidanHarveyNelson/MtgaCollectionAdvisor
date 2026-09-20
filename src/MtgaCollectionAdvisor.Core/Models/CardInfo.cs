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
/// collection dump and wildcard economy.
/// </summary>
public sealed record CardInfo(
    int GrpId,
    string Name,
    string SetCode,
    string ManaCost,
    string Colors,
    CardRarity Rarity,
    bool StandardLegal,
    bool PioneerLegal);
