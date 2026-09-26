using Microsoft.Data.Sqlite;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Releases a throwaway database file so it can be deleted (#41). Windows keeps a file locked
/// while a pooled connection holds it, so its pool must be cleared first - but only its own:
/// <see cref="SqliteConnection.ClearAllPools"/> also closes the connections other test classes,
/// running in parallel, are still using, and they fail at random with ObjectDisposedException.
/// </summary>
internal static class TestDatabaseFiles
{
    /// <summary>Closes the pooled connections to this file only. The pool is keyed by connection string, which is <see cref="Storage.Database"/>'s.</summary>
    public static void Release(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>Releases the file, then deletes it with its WAL and shared-memory files.</summary>
    public static void Delete(string path)
    {
        Release(path);
        foreach (var file in (string[])[path, path + "-wal", path + "-shm"])
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
