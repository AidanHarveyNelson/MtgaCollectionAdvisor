using System.Text.RegularExpressions;

namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>
/// Works out where a video's decklist can be recovered from, using only its description.
/// In priority order: an Arena export pasted into it, an Archidekt link, a link to a site
/// the app does not read, or nothing.
/// </summary>
public static partial class VideoDeckExtractor
{
    /// <summary>A real list has dozens of card lines; timestamps and top-5 lists have a few.</summary>
    public const int MinimumCardLines = 15;

    // Sites that block automated reads (Cloudflare) or have no public API. Their links are
    // only ever opened for the user.
    private static readonly (string Site, Regex Pattern)[] ExternalSites =
    [
        ("AetherHub", new Regex(@"https?://(?:www\.)?aetherhub\.com/\S+", RegexOptions.IgnoreCase)),
        ("Moxfield", new Regex(@"https?://(?:www\.)?moxfield\.com/decks/[\w-]+", RegexOptions.IgnoreCase)),
        ("Untapped", new Regex(@"https?://(?:www\.)?(?:mtga\.)?untapped\.gg/\S*deck\S*", RegexOptions.IgnoreCase)),
        ("MTGGoldfish", new Regex(@"https?://(?:www\.)?mtggoldfish\.com/(?:deck|archetype)/\S+", RegexOptions.IgnoreCase)),
    ];

    public static VideoDeckSource Extract(string description)
    {
        var lines = description.Split('\n').Select(l => l.Trim()).ToList();

        if (lines.Count(l => CardLine().IsMatch(l)) >= MinimumCardLines)
        {
            var list = new List<string>();
            foreach (var line in lines)
            {
                if (CardLine().IsMatch(line)) list.Add(line);
                else if (CanonicalHeader(line) is { } header) list.Add(header);
            }
            return new VideoDeckSource(DeckSourceKind.InlineList, Decklist: string.Join('\n', list));
        }

        if (ArchidektLink().Match(description) is { Success: true } archidekt
            && int.TryParse(archidekt.Groups[1].Value, out var id))
        {
            return new VideoDeckSource(DeckSourceKind.Archidekt, ArchidektId: id);
        }

        foreach (var (site, pattern) in ExternalSites)
        {
            if (pattern.Match(description) is { Success: true } link)
            {
                return new VideoDeckSource(DeckSourceKind.External, ExternalSite: site, ExternalUrl: link.Value);
            }
        }

        return VideoDeckSource.None;
    }

    /// <summary>
    /// Creators decorate their section headers ("🧰 SIDEBOARD", "🃏 DECKLIST"); the parser
    /// only knows the plain words, and missing a Sideboard header counts it as mainboard.
    /// A line whose letters alone spell a section name becomes that section's header.
    /// </summary>
    private static string? CanonicalHeader(string line)
    {
        if (line.Length > 40) return null;

        var letters = new string(line.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return letters switch
        {
            "deck" or "decklist" or "maindeck" or "mainboard" => "Deck",
            "sideboard" => "Sideboard",
            "companion" => "Companion",
            "commander" => "Commander",
            _ => null
        };
    }

    [GeneratedRegex(@"^\d{1,2}\s+[A-Z]")]
    private static partial Regex CardLine();

    [GeneratedRegex(@"archidekt\.com/decks/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArchidektLink();
}
