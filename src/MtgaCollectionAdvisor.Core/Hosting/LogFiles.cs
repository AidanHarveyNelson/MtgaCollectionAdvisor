using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MtgaCollectionAdvisor.Core.Hosting;

/// <summary>
/// Where the app's log files live and what they look like (#52). The installed app has no
/// console, so a daily file next to the database is what a player can send when something goes
/// wrong. It holds warnings, errors and a few startup facts, never their collection or decks.
/// </summary>
public static partial class LogFiles
{
    public const int KeepDays = 7;
    public const long MaxBytesPerFile = 5 * 1024 * 1024;

    /// <summary>The app's own startup lines, the only Information entries the file keeps.</summary>
    public const string StartupCategory = "MtgaDeckAdvisor.Startup";

    private const string Prefix = "advisor-";

    [GeneratedRegex(@"^advisor-(\d{4}-\d{2}-\d{2})\.log$")]
    private static partial Regex LogFileName();

    /// <summary>Next to the database, so a test copy (MTGA_ADVISOR_DB_PATH) keeps its own logs.</summary>
    public static string FolderFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "logs");

    public static string FileName(DateOnly day) =>
        $"{Prefix}{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

    /// <summary>The app's log files older than <see cref="KeepDays"/>; anything else is never returned.</summary>
    public static IReadOnlyList<string> ExpiredFiles(IEnumerable<string> fileNames, DateOnly today)
    {
        var oldestKept = today.AddDays(-KeepDays);
        return fileNames
            .Where(name => LogFileName().Match(name) is { Success: true } m
                           && DateOnly.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out var day)
                           && day < oldestKept)
            .ToList();
    }

    /// <summary>One entry: "2026-09-25 18:04:12.345 WARN  Category: message", then the exception.</summary>
    public static string FormatEntry(DateTimeOffset at, string level, string category, string message, Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(at.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ')
            .Append(category).Append(": ").Append(message);

        if (exception is not null) entry.AppendLine().Append(exception);
        return entry.AppendLine().ToString();
    }

    /// <summary>ILogger's LogLevel, by its number, as a fixed-width tag.</summary>
    public static string LevelText(int level) => level switch
    {
        0 => "TRACE",
        1 => "DEBUG",
        2 => "INFO ",
        3 => "WARN ",
        4 => "ERROR",
        5 => "CRIT ",
        _ => "?????",
    };
}
