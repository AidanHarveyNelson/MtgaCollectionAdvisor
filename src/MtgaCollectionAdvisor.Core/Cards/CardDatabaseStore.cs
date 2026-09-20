using MtgaCollectionAdvisor.Core.Models;
using Npgsql;
using NpgsqlTypes;

namespace MtgaCollectionAdvisor.Core.Cards;

public sealed class CardDatabaseStore(NpgsqlDataSource dataSource)
{
    public async Task ReplaceAllAsync(IAsyncEnumerable<CardInfo> cards, CancellationToken ct = default)
    {
        var importedAt = DateTimeOffset.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = new NpgsqlCommand("TRUNCATE TABLE cards", connection, transaction))
        {
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            """
            COPY cards (grp_id, name, set_code, mana_cost, colors, rarity, standard_legal, pioneer_legal, updated_at)
            FROM STDIN (FORMAT BINARY)
            """, ct))
        {
            await foreach (var card in cards.WithCancellation(ct))
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(card.GrpId, ct);
                await writer.WriteAsync(card.Name, ct);
                await writer.WriteAsync(card.SetCode, ct);
                await writer.WriteAsync(card.ManaCost, ct);
                await writer.WriteAsync(card.Colors, ct);
                await writer.WriteAsync(card.Rarity.ToString(), ct);
                await writer.WriteAsync(card.StandardLegal, ct);
                await writer.WriteAsync(card.PioneerLegal, ct);
                await writer.WriteAsync(importedAt, NpgsqlDbType.TimestampTz, ct);
            }
            await writer.CompleteAsync(ct);
        }

        await using (var state = new NpgsqlCommand(
            """
            INSERT INTO card_import_state (id, last_imported) VALUES (1, @at)
            ON CONFLICT (id) DO UPDATE SET last_imported = EXCLUDED.last_imported
            """, connection, transaction))
        {
            state.Parameters.AddWithValue("at", importedAt);
            await state.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<DateTimeOffset?> GetLastImportedAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT last_imported FROM card_import_state WHERE id = 1", connection);
        var result = await command.ExecuteScalarAsync(ct);
        return result is DateTimeOffset dto ? dto : null;
    }

    public async Task<IReadOnlyList<CardInfo>> FindByNameAsync(string name, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT grp_id, name, set_code, mana_cost, colors, rarity, standard_legal, pioneer_legal FROM cards WHERE lower(name) = lower(@name)",
            connection);
        command.Parameters.AddWithValue("name", name);

        var results = new List<CardInfo>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadCard(reader));
        }
        return results;
    }

    private static CardInfo ReadCard(NpgsqlDataReader reader) => new(
        GrpId: reader.GetInt32(0),
        Name: reader.GetString(1),
        SetCode: reader.GetString(2),
        ManaCost: reader.GetString(3),
        Colors: reader.GetString(4),
        Rarity: Enum.Parse<CardRarity>(reader.GetString(5)),
        StandardLegal: reader.GetBoolean(6),
        PioneerLegal: reader.GetBoolean(7));
}
