namespace MtgaCollectionAdvisor.Core.Models;

/// <summary>
/// Per-card gap between what the deck needs and what the collection owns.
/// GrpId is null when the card could not be matched to an Arena printing at all
/// (i.e. it does not exist on Arena, regardless of collection).
/// </summary>
public sealed record CardGap(
    string CardName,
    DeckBoard Board,
    int Needed,
    int Owned,
    int? GrpId,
    CardRarity Rarity)
{
    public int Missing => Math.Max(0, Needed - Owned);
    public bool AvailableOnArena => GrpId is not null;
}

public sealed record WildcardNeed(int Commons, int Uncommons, int Rares, int Mythics)
{
    public int Total => Commons + Uncommons + Rares + Mythics;

    public static WildcardNeed Zero { get; } = new(0, 0, 0, 0);

    public static WildcardNeed FromGap(CardGap gap)
    {
        if (gap.Missing <= 0 || !gap.AvailableOnArena) return Zero;
        return gap.Rarity switch
        {
            CardRarity.Common => new WildcardNeed(gap.Missing, 0, 0, 0),
            CardRarity.Uncommon => new WildcardNeed(0, gap.Missing, 0, 0),
            CardRarity.Rare => new WildcardNeed(0, 0, gap.Missing, 0),
            CardRarity.Mythic => new WildcardNeed(0, 0, 0, gap.Missing),
            _ => Zero
        };
    }

    public WildcardNeed Add(WildcardNeed other) => new(
        Commons + other.Commons,
        Uncommons + other.Uncommons,
        Rares + other.Rares,
        Mythics + other.Mythics);

    /// <summary>
    /// Wildcards are rarity-specific in Arena (a Rare wildcard can't craft a Mythic),
    /// so affordability must be checked per rarity, not just by total count.
    /// </summary>
    public bool IsAffordableWith(WildcardInventory wallet) =>
        wallet.Commons >= Commons && wallet.Uncommons >= Uncommons &&
        wallet.Rares >= Rares && wallet.Mythics >= Mythics;
}

public sealed record DeckAnalysisResult(
    CandidateDeck Deck,
    WildcardNeed Needed,
    int OwnedCopies,
    int TotalCopies,
    IReadOnlyList<CardGap> Gaps,
    IReadOnlyList<string> UnavailableOnArena,
    IReadOnlyList<string> IllegalInFormat,
    string Colors)
{
    public double OwnedFraction => TotalCopies == 0 ? 0 : (double)OwnedCopies / TotalCopies;
    public bool FullyPlayableOnArena => UnavailableOnArena.Count == 0;

    /// <summary>
    /// Deck sources let users file a deck under any format they like, so a "Standard"
    /// deck may well contain cards that rotated out years ago.
    /// </summary>
    public bool LegalInFormat => IllegalInFormat.Count == 0;
}
