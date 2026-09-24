namespace MtgaCollectionAdvisor.Core.Storage;

/// <summary>
/// The database was written by a newer build than this one. Its schema may hold changes this
/// build does not know, so it is left exactly as it is rather than read or written.
/// </summary>
public sealed class SchemaTooNewException(int databaseVersion, int latestKnown) : Exception(
    $"This database was saved by a newer version of MTGA Deck Advisor (schema {databaseVersion}; " +
    $"this version knows up to {latestKnown}). Install the newer version again. The database was not changed.")
{
    public int DatabaseVersion { get; } = databaseVersion;
    public int LatestKnown { get; } = latestKnown;
}
