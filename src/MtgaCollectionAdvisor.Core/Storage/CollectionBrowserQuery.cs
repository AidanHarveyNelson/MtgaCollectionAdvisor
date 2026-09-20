using MtgaCollectionAdvisor.Core.Models;
using Npgsql;

namespace MtgaCollectionAdvisor.Core.Storage;

public sealed record CollectionBrowserRow(
    int GrpId,
    string Name,
    string SetCode,
    string ManaCost,
    string Colors,
    CardRarity Rarity,
    bool StandardLegal,
    bool PioneerLegal,
    int Owned);

/// <summary>
/// Backing query for the collection browser screen: every known card joined with how
/// many copies the user owns (0 if none), for searching/filtering the full collection.
/// </summary>
public sealed class CollectionBrowserQuery(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<CollectionBrowserRow>> LoadAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.grp_id, c.name, c.set_code, c.mana_cost, c.colors, c.rarity,
                   c.standard_legal, c.pioneer_legal, COALESCE(cc.quantity, 0) AS owned
            FROM cards c
            LEFT JOIN collection_cards cc ON cc.grp_id = c.grp_id
            ORDER BY c.name, c.set_code
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<CollectionBrowserRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new CollectionBrowserRow(
                GrpId: reader.GetInt32(0),
                Name: reader.GetString(1),
                SetCode: reader.GetString(2),
                ManaCost: reader.GetString(3),
                Colors: reader.GetString(4),
                Rarity: Enum.Parse<CardRarity>(reader.GetString(5)),
                StandardLegal: reader.GetBoolean(6),
                PioneerLegal: reader.GetBoolean(7),
                Owned: reader.GetInt32(8)));
        }
        return rows;
    }
}
