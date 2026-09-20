using MtgaCollectionAdvisor.Core.Models;
using Npgsql;

namespace MtgaCollectionAdvisor.Core.Storage;

public sealed class CollectionStore(NpgsqlDataSource dataSource)
{
    public async Task SaveCollectionAsync(IReadOnlyDictionary<int, int> ownedByGrpId, CancellationToken ct = default)
    {
        var syncedAt = DateTimeOffset.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = new NpgsqlCommand("TRUNCATE TABLE collection_cards", connection, transaction))
        {
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY collection_cards (grp_id, quantity, synced_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var (grpId, qty) in ownedByGrpId)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(grpId, ct);
                await writer.WriteAsync(qty, ct);
                await writer.WriteAsync(syncedAt, ct);
            }
            await writer.CompleteAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task SaveWildcardsAsync(WildcardInventory inventory, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO wildcard_inventory (id, commons, uncommons, rares, mythics, synced_at)
            VALUES (1, @commons, @uncommons, @rares, @mythics, @syncedAt)
            ON CONFLICT (id) DO UPDATE SET
                commons = EXCLUDED.commons,
                uncommons = EXCLUDED.uncommons,
                rares = EXCLUDED.rares,
                mythics = EXCLUDED.mythics,
                synced_at = EXCLUDED.synced_at;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("commons", inventory.Commons);
        command.Parameters.AddWithValue("uncommons", inventory.Uncommons);
        command.Parameters.AddWithValue("rares", inventory.Rares);
        command.Parameters.AddWithValue("mythics", inventory.Mythics);
        command.Parameters.AddWithValue("syncedAt", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<CollectionSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var owned = new Dictionary<int, int>();
        DateTimeOffset syncedAt = DateTimeOffset.MinValue;

        await using (var command = new NpgsqlCommand("SELECT grp_id, quantity, synced_at FROM collection_cards", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                owned[reader.GetInt32(0)] = reader.GetInt32(1);
                var rowSynced = reader.GetFieldValue<DateTimeOffset>(2);
                if (rowSynced > syncedAt) syncedAt = rowSynced;
            }
        }

        var wildcards = WildcardInventory.Empty;
        await using (var command = new NpgsqlCommand(
            "SELECT commons, uncommons, rares, mythics, synced_at FROM wildcard_inventory WHERE id = 1", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                wildcards = new WildcardInventory(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                var rowSynced = reader.GetFieldValue<DateTimeOffset>(4);
                if (rowSynced > syncedAt) syncedAt = rowSynced;
            }
        }

        return new CollectionSnapshot(owned, wildcards, syncedAt);
    }
}
