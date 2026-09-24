namespace MtgaCollectionAdvisor.Core.Storage;

/// <summary>One step of the schema, applied in a single transaction by <see cref="SchemaMigrator"/>.</summary>
public sealed record Migration(int Version, string Name, string Sql);

/// <summary>What a run of <see cref="SchemaMigrator"/> did; <c>BackupPath</c> is null when no backup was taken.</summary>
public sealed record MigrationResult(int FromVersion, int ToVersion, string? BackupPath);
