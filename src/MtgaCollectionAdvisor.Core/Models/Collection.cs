namespace MtgaCollectionAdvisor.Core.Models;

public sealed record WildcardInventory(
    int Commons,
    int Uncommons,
    int Rares,
    int Mythics)
{
    public static readonly WildcardInventory Empty = new(0, 0, 0, 0);
}

/// <summary>
/// The user's owned card collection: grpId -> quantity owned, plus wildcard inventory,
/// as last captured from Player.log.
/// </summary>
public sealed record CollectionSnapshot(
    IReadOnlyDictionary<int, int> OwnedByGrpId,
    WildcardInventory Wildcards,
    DateTimeOffset SyncedAt)
{
    public static readonly CollectionSnapshot Empty = new(
        new Dictionary<int, int>(),
        WildcardInventory.Empty,
        DateTimeOffset.MinValue);

    public int OwnedQuantity(int grpId) => OwnedByGrpId.TryGetValue(grpId, out var qty) ? qty : 0;
}
