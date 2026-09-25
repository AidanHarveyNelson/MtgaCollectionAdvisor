namespace MtgaCollectionAdvisor.Core.Logs;

/// <summary>
/// MTG Arena writes whether "Detailed Logs (Plugin Support)" is on as a plain line near the top
/// of each session's Player.log. Wildcard totals and saved decks are only logged when it is on,
/// so it is what tells a player why their wildcards are unknown (#57).
/// </summary>
public static class DetailedLogsLine
{
    private const string Prefix = "DETAILED LOGS:";

    /// <summary>True for ENABLED, false for DISABLED, null for any other line.</summary>
    public static bool? Parse(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        return text[Prefix.Length..].Trim() switch
        {
            "ENABLED" => true,
            "DISABLED" => false,
            _ => null,
        };
    }
}
