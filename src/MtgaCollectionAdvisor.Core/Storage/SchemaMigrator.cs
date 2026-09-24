using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MtgaCollectionAdvisor.Core.Storage;

/// <summary>
/// Brings a database up to the newest schema in <see cref="Migrations"/>, tracking progress in
/// <c>PRAGMA user_version</c>. A user who installs a new version keeps their data: the file is
/// backed up before anything changes, each migration commits or rolls back whole, and a
/// database from a newer build is refused untouched.
/// </summary>
public static class SchemaMigrator
{
    public const int BackupsKept = 3;

    public static Task<MigrationResult> MigrateAsync(Database database, CancellationToken ct = default)
        => MigrateAsync(database, Migrations.All, ct);

    // Public so tests can append a migration that fails; the app always uses Migrations.All.
    public static async Task<MigrationResult> MigrateAsync(
        Database database, IReadOnlyList<Migration> migrations, CancellationToken ct = default)
    {
        EnsureContiguous(migrations);
        var latest = migrations[^1].Version;

        // Read before Database.OpenAsync, which switches the file to WAL: a database this build
        // must refuse is not to be written at all, not even its journal mode.
        var version = await ReadVersionAsync(database.FilePath, ct);
        if (version > latest) throw new SchemaTooNewException(version, latest);

        var pending = migrations.Where(m => m.Version > version).ToList();
        if (pending.Count == 0) return new MigrationResult(version, version, null);

        await using var connection = await database.OpenAsync(ct);

        // A table rebuild (create, copy, drop, rename) breaks foreign keys midway. The pragma is
        // ignored inside a transaction, so it is switched off here and checked per migration.
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys = OFF;", ct);

        var backupPath = await HasTablesAsync(connection, ct)
            ? await BackUpAsync(connection, database.FilePath, version, ct)
            : null;

        foreach (var migration in pending)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            try
            {
                await ExecuteAsync(connection, transaction, migration.Sql, ct);
                await ExecuteAsync(connection, transaction,
                    $"PRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)};", ct);
                await EnsureForeignKeysHoldAsync(connection, transaction, ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    $"Updating the database to schema {migration.Version} ({migration.Name}) failed; it is " +
                    $"still at schema {migration.Version - 1}" +
                    (backupPath is null ? "." : $", and a copy from before the update is at {backupPath}."),
                    ex);
            }
        }

        return new MigrationResult(version, latest, backupPath);
    }

    private static void EnsureContiguous(IReadOnlyList<Migration> migrations)
    {
        if (migrations.Count == 0) throw new ArgumentException("There are no migrations.", nameof(migrations));

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new ArgumentException(
                    $"Migrations must be numbered 1, 2, 3 and so on in order; position {i} holds {migrations[i].Version}.",
                    nameof(migrations));
        }
    }

    private static async Task<int> ReadVersionAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return 0;

        // Unpooled, so the file is not held open by this check after it returns.
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<string> BackUpAsync(
        SqliteConnection connection, string databasePath, int version, CancellationToken ct)
    {
        var backupPath = $"{databasePath}.backup-v{version.ToString(CultureInfo.InvariantCulture)}";

        // VACUUM INTO refuses a target that exists, and takes the path only as a literal.
        File.Delete(backupPath);
        await ExecuteAsync(connection, null, $"VACUUM INTO '{backupPath.Replace("'", "''")}';", ct);

        PruneBackups(databasePath);
        return backupPath;
    }

    private static void PruneBackups(string databasePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var stale = directory.GetFiles($"{Path.GetFileName(databasePath)}.backup-v*")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(BackupsKept);

        foreach (var file in stale) file.Delete();
    }

    private static async Task EnsureForeignKeysHoldAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            throw new InvalidOperationException(
                $"Table {reader.GetString(0)} has a row pointing at a missing row in {reader.GetString(2)}.");
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
