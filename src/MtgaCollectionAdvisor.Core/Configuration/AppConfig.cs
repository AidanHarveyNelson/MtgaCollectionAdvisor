namespace MtgaCollectionAdvisor.Core.Configuration;

/// <summary>
/// App-wide configuration. Data lives in a local SQLite file under the user's
/// LocalApplicationData, so a published build needs no database server or setup.
/// </summary>
public sealed record AppConfig(string? DatabasePathOverride, string? PlayerLogPathOverride)
{
    public static AppConfig Default { get; } = new(
        DatabasePathOverride: Environment.GetEnvironmentVariable("MTGA_ADVISOR_DB_PATH"),
        PlayerLogPathOverride: Environment.GetEnvironmentVariable("MTGA_ADVISOR_PLAYERLOG_PATH"));
}
