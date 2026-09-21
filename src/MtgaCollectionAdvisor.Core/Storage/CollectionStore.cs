using Microsoft.Data.Sqlite;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Storage;

public sealed class CollectionStore(Database database)
{
    /// <summary>
    /// Sets how many copies of one specific card the user owns - the manual, one-card-
    /// at-a-time equivalent of a full collection import.
    /// </summary>
    public async Task SetOwnedAsync(int grpId, int quantity, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        if (quantity <= 0)
        {
            command.CommandText = "DELETE FROM collection_cards WHERE grp_id = $grpId";
            command.Parameters.AddWithValue("$grpId", grpId);
        }
        else
        {
            command.CommandText = """
                INSERT INTO collection_cards (grp_id, quantity, synced_at)
                VALUES ($grpId, $quantity, $syncedAt)
                ON CONFLICT (grp_id) DO UPDATE SET quantity = excluded.quantity, synced_at = excluded.synced_at
                """;
            command.Parameters.AddWithValue("$grpId", grpId);
            command.Parameters.AddWithValue("$quantity", quantity);
            command.Parameters.AddWithValue("$syncedAt", DateTimeOffset.UtcNow.ToString("O"));
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveCollectionAsync(IReadOnlyDictionary<int, int> ownedByGrpId, CancellationToken ct = default)
    {
        var syncedAt = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM collection_cards";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO collection_cards (grp_id, quantity, synced_at) VALUES ($grpId, $quantity, $syncedAt)";
            var grpIdParameter = insert.Parameters.Add("$grpId", SqliteType.Integer);
            var quantityParameter = insert.Parameters.Add("$quantity", SqliteType.Integer);
            insert.Parameters.AddWithValue("$syncedAt", syncedAt);

            foreach (var (grpId, quantity) in ownedByGrpId)
            {
                grpIdParameter.Value = grpId;
                quantityParameter.Value = quantity;
                await insert.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    public async Task SaveWildcardsAsync(WildcardInventory inventory, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO wildcard_inventory (id, commons, uncommons, rares, mythics, synced_at)
            VALUES (1, $commons, $uncommons, $rares, $mythics, $syncedAt)
            ON CONFLICT (id) DO UPDATE SET
                commons = excluded.commons,
                uncommons = excluded.uncommons,
                rares = excluded.rares,
                mythics = excluded.mythics,
                synced_at = excluded.synced_at
            """;
        command.Parameters.AddWithValue("$commons", inventory.Commons);
        command.Parameters.AddWithValue("$uncommons", inventory.Uncommons);
        command.Parameters.AddWithValue("$rares", inventory.Rares);
        command.Parameters.AddWithValue("$mythics", inventory.Mythics);
        command.Parameters.AddWithValue("$syncedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<CollectionSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);

        var owned = new Dictionary<int, int>();
        var syncedAt = DateTimeOffset.MinValue;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT grp_id, quantity, synced_at FROM collection_cards";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                owned[reader.GetInt32(0)] = reader.GetInt32(1);
                if (DateTimeOffset.TryParse(reader.GetString(2), out var rowSynced) && rowSynced > syncedAt)
                {
                    syncedAt = rowSynced;
                }
            }
        }

        var wildcards = WildcardInventory.Empty;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT commons, uncommons, rares, mythics, synced_at FROM wildcard_inventory WHERE id = 1";
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                wildcards = new WildcardInventory(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                if (DateTimeOffset.TryParse(reader.GetString(4), out var rowSynced) && rowSynced > syncedAt)
                {
                    syncedAt = rowSynced;
                }
            }
        }

        return new CollectionSnapshot(owned, wildcards, syncedAt);
    }
}
