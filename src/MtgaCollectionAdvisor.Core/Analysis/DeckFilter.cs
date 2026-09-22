using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Analysis;

/// <summary>Which half of the pool a list is showing.</summary>
public enum DeckSourceFilter
{
    Any,

    /// <summary>Decks pulled from a public source.</summary>
    Fetched,

    /// <summary>Decks the user added by hand.</summary>
    User
}

/// <summary>
/// Everything the deck list can be narrowed by. Lives here rather than in the UI so the
/// rules can be tested without rendering a component or touching a database.
/// </summary>
public sealed record DeckFilterCriteria
{
    public IReadOnlySet<char> Colors { get; init; } = new HashSet<char>();

    /// <summary>Match the colour combination exactly, instead of "includes these colours".</summary>
    public bool ExactColors { get; init; }

    public bool OnlyCraftable { get; init; }
    public int? MaxWildcards { get; init; }
    public string NameSearch { get; init; } = "";

    /// <summary>Every one of these cards must be in the deck.</summary>
    public IReadOnlyList<string> ContainsCards { get; init; } = [];

    /// <summary>None of these cards may be in the deck.</summary>
    public IReadOnlyList<string> ExcludesCards { get; init; } = [];

    /// <summary>
    /// Keep decks that are illegal in the format or use cards missing from Arena. The
    /// recommended list leaves this off; a list of the user's own decks turns it on,
    /// because a deck someone imported by hand should never silently disappear.
    ///
    /// Deliberately absent from <see cref="IsEmpty"/>: this is which list is being shown,
    /// not a filter the user chose, so it must not offer them a "clear filters" link.
    /// </summary>
    public bool IncludeUnplayable { get; init; }

    /// <summary>
    /// Show only fetched decks, only the user's own, or both. Absent from
    /// <see cref="IsEmpty"/> for the same reason as <see cref="IncludeUnplayable"/>.
    /// </summary>
    public DeckSourceFilter Source { get; init; } = DeckSourceFilter.Any;

    public bool IsEmpty =>
        Colors.Count == 0
        && !OnlyCraftable
        && MaxWildcards is null
        && string.IsNullOrWhiteSpace(NameSearch)
        && ContainsCards.Count == 0
        && ExcludesCards.Count == 0;
}

public static class DeckFilter
{
    public static IReadOnlyList<DeckAnalysisResult> Apply(
        IEnumerable<DeckAnalysisResult> decks,
        DeckFilterCriteria criteria,
        WildcardInventory wallet)
    {
        IEnumerable<DeckAnalysisResult> query = decks;

        query = criteria.Source switch
        {
            DeckSourceFilter.User => query.Where(d => d.Deck.IsUserDeck),
            DeckSourceFilter.Fetched => query.Where(d => !d.Deck.IsUserDeck),
            _ => query
        };

        // Deck sources let anyone file any list under any format, and a list can name a
        // card that never came to Arena. Dropping those is right for suggestions and
        // wrong for a deck the user asked for, so it is a switch rather than a rule.
        if (!criteria.IncludeUnplayable)
        {
            query = query.Where(d => d.LegalInFormat && d.FullyPlayableOnArena);
        }

        if (criteria.Colors.Count > 0)
        {
            query = criteria.ExactColors
                ? query.Where(d => d.Colors.Length == criteria.Colors.Count && d.Colors.All(criteria.Colors.Contains))
                : query.Where(d => criteria.Colors.All(c => d.Colors.Contains(c)));
        }

        if (criteria.OnlyCraftable)
        {
            query = query.Where(d => d.Needed.IsAffordableWith(wallet));
        }

        if (criteria.MaxWildcards is { } max)
        {
            query = query.Where(d => d.Needed.Total <= Math.Max(0, max));
        }

        if (!string.IsNullOrWhiteSpace(criteria.NameSearch))
        {
            query = query.Where(d => d.Deck.Name.Contains(criteria.NameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // Every required card must be present; any excluded card disqualifies the deck.
        if (criteria.ContainsCards.Count > 0)
        {
            query = query.Where(d => criteria.ContainsCards.All(card => PlaysCard(d, card)));
        }

        if (criteria.ExcludesCards.Count > 0)
        {
            query = query.Where(d => !criteria.ExcludesCards.Any(card => PlaysCard(d, card)));
        }

        return query.ToList();
    }

    /// <summary>
    /// Sideboard cards count as played: they still cost wildcards, and a deck that only
    /// sideboards a card is still a deck built around owning it.
    /// </summary>
    private static bool PlaysCard(DeckAnalysisResult deck, string cardName) =>
        deck.Deck.Cards.Any(c => c.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase));
}
