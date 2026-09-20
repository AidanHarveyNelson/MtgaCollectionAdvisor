namespace MtgaCollectionAdvisor.Core.Models;

public enum DeckBoard
{
    Main,
    Sideboard
}

public sealed record DeckCardRef(string Name, int Quantity, DeckBoard Board);

/// <summary>
/// A candidate decklist fetched from a public source (Moxfield), identified by
/// card name rather than grpId - names are resolved against the local card
/// database at analysis time so the same deck can be matched across sets.
/// </summary>
public sealed record CandidateDeck(
    string SourceId,
    string Name,
    string Url,
    string FormatKey,
    int Popularity,
    IReadOnlyList<DeckCardRef> Cards,
    DateTimeOffset FetchedAt);
