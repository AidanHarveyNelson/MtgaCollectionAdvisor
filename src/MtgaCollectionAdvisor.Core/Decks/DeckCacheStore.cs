using MtgaCollectionAdvisor.Core.Models;
using Npgsql;

namespace MtgaCollectionAdvisor.Core.Decks;

public sealed class DeckCacheStore(NpgsqlDataSource dataSource)
{
    public async Task SaveAsync(FormatDefinition format, IReadOnlyList<CandidateDeck> decks, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = new NpgsqlCommand(
            "DELETE FROM decks WHERE format_key = @format", connection, transaction))
        {
            clear.Parameters.AddWithValue("format", format.Key);
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var deck in decks)
        {
            await using (var insertDeck = new NpgsqlCommand(
                """
                INSERT INTO decks (source_id, format_key, name, url, popularity, fetched_at)
                VALUES (@id, @format, @name, @url, @popularity, @fetchedAt)
                """, connection, transaction))
            {
                insertDeck.Parameters.AddWithValue("id", deck.SourceId);
                insertDeck.Parameters.AddWithValue("format", format.Key);
                insertDeck.Parameters.AddWithValue("name", deck.Name);
                insertDeck.Parameters.AddWithValue("url", deck.Url);
                insertDeck.Parameters.AddWithValue("popularity", deck.Popularity);
                insertDeck.Parameters.AddWithValue("fetchedAt", deck.FetchedAt);
                await insertDeck.ExecuteNonQueryAsync(ct);
            }

            foreach (var card in deck.Cards)
            {
                await using var insertCard = new NpgsqlCommand(
                    """
                    INSERT INTO deck_cards (source_id, card_name, board, quantity)
                    VALUES (@id, @name, @board, @qty)
                    ON CONFLICT (source_id, card_name, board) DO UPDATE SET quantity = EXCLUDED.quantity
                    """, connection, transaction);
                insertCard.Parameters.AddWithValue("id", deck.SourceId);
                insertCard.Parameters.AddWithValue("name", card.Name);
                insertCard.Parameters.AddWithValue("board", card.Board.ToString());
                insertCard.Parameters.AddWithValue("qty", card.Quantity);
                await insertCard.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<CandidateDeck>> LoadAsync(FormatDefinition format, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var decksById = new Dictionary<string, (string Name, string Url, int Popularity, DateTimeOffset FetchedAt, List<DeckCardRef> Cards)>();

        await using (var command = new NpgsqlCommand(
            "SELECT source_id, name, url, popularity, fetched_at FROM decks WHERE format_key = @format", connection))
        {
            command.Parameters.AddWithValue("format", format.Key);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                decksById[reader.GetString(0)] = (
                    reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetFieldValue<DateTimeOffset>(4), []);
            }
        }

        if (decksById.Count == 0) return [];

        await using (var command = new NpgsqlCommand(
            """
            SELECT dc.source_id, dc.card_name, dc.board, dc.quantity
            FROM deck_cards dc
            JOIN decks d ON d.source_id = dc.source_id
            WHERE d.format_key = @format
            """, connection))
        {
            command.Parameters.AddWithValue("format", format.Key);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var sourceId = reader.GetString(0);
                if (!decksById.TryGetValue(sourceId, out var deck)) continue;
                var board = Enum.Parse<DeckBoard>(reader.GetString(2));
                deck.Cards.Add(new DeckCardRef(reader.GetString(1), reader.GetInt32(3), board));
            }
        }

        return decksById
            .Select(kv => new CandidateDeck(kv.Key, kv.Value.Name, kv.Value.Url, format.Key, kv.Value.Popularity, kv.Value.Cards, kv.Value.FetchedAt))
            .ToList();
    }

    public async Task<DateTimeOffset?> GetLastFetchedAsync(FormatDefinition format, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT MAX(fetched_at) FROM decks WHERE format_key = @format", connection);
        command.Parameters.AddWithValue("format", format.Key);
        var result = await command.ExecuteScalarAsync(ct);
        return result is DateTimeOffset dto ? dto : null;
    }
}
