namespace MtgaCollectionAdvisor.Core.Configuration;

/// <summary>
/// App-wide configuration. The Postgres instance reused here is the existing local
/// "trust_postgres" dev container; this app gets its own database
/// (mtga_collection_advisor) on that same server so it never touches other projects'
/// tables.
/// </summary>
public sealed record AppConfig(string PostgresConnectionString, string? PlayerLogPathOverride)
{
    public static AppConfig Default { get; } = new(
        PostgresConnectionString:
            Environment.GetEnvironmentVariable("MTGA_ADVISOR_CONNSTRING")
            ?? "Host=localhost;Port=5432;Database=mtga_collection_advisor;Username=trust_user;Password=trust_dev_pass",
        PlayerLogPathOverride: Environment.GetEnvironmentVariable("MTGA_ADVISOR_PLAYERLOG_PATH"));
}
