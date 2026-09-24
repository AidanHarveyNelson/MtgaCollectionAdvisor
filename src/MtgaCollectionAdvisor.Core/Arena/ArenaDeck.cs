namespace MtgaCollectionAdvisor.Core.Arena;

public sealed record ArenaCard(int GrpId, int Quantity);

/// <summary>
/// A deck saved inside MTG Arena, as Arena writes it to Player.log at login. Cards are
/// Arena ids; names come from the local card database when the deck is written out.
/// </summary>
public sealed record ArenaDeck(
    string Id,
    string Name,
    string Format,
    bool IsWizardsDeck,
    IReadOnlyList<ArenaCard> Commander,
    IReadOnlyList<ArenaCard> Companion,
    IReadOnlyList<ArenaCard> Main,
    IReadOnlyList<ArenaCard> Sideboard);

/// <summary>The last deck list Arena logged, and when the app saw it.</summary>
public sealed record ArenaDeckSnapshot(IReadOnlyList<ArenaDeck> Decks, DateTimeOffset CapturedAt);
