namespace MtgaCollectionAdvisor.Core.Models;

/// <summary>What Player.log said about Arena's "Detailed Logs (Plugin Support)" option.</summary>
public enum DetailedLogs { Unknown, Enabled, Disabled }

/// <summary>
/// The one sentence the app shows while the wildcard totals are unknown (#57): what the player
/// has to do in MTG Arena for them to be read, based on what its log says.
/// </summary>
public static class WildcardStatus
{
    public static string? Hint(bool wildcardsKnown, DetailedLogs detailedLogs, bool logExists)
    {
        if (wildcardsKnown) return null;

        return (detailedLogs, logExists) switch
        {
            (DetailedLogs.Disabled, _) =>
                "Turn on Detailed Logs in MTG Arena (Options → Account → Detailed Logs (Plugin Support)), then restart the game.",
            (DetailedLogs.Enabled, true) =>
                "Log in to MTG Arena: your wildcards are read when you log in.",
            _ =>
                "Open MTG Arena with Options → Account → Detailed Logs (Plugin Support) turned on.",
        };
    }
}
