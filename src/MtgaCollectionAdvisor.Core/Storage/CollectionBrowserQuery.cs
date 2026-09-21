using MtgaCollectionAdvisor.Core.Models;

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
/// Every known card joined with how many copies the user owns, for inspecting the
/// captured collection.
/// </summary>
public sealed class CollectionBrowserQuery(Database database)
{
    public async Task<IReadOnlyList<CollectionBrowserRow>> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.grp_id, c.name, c.set_code, c.mana_cost, c.colors, c.rarity,
                   c.standard_legal, c.pioneer_legal, COALESCE(cc.quantity, 0) AS owned
            FROM cards c
            LEFT JOIN collection_cards cc ON cc.grp_id = c.grp_id
            ORDER BY c.name, c.set_code
            """;

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
                StandardLegal: reader.GetInt32(6) == 1,
                PioneerLegal: reader.GetInt32(7) == 1,
                Owned: reader.GetInt32(8)));
        }
        return rows;
    }
}
