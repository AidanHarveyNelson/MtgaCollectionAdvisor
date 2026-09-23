using System.Text.Json;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Arena;

/// <summary>
/// The last deck list Arena logged. Player.log is rewritten each time Arena starts, so
/// without this the decks could only be exported while the log still held the login.
/// </summary>
public sealed class ArenaDeckStore(Database database)
{
    /// <summary>Replaces the stored list whole: a deck deleted in Arena goes with the next capture.</summary>
    public async Task ReplaceAsync(IReadOnlyList<ArenaDeck> decks, DateTimeOffset capturedAt, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM arena_decks";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT OR REPLACE INTO arena_decks (deck_id, name, format, wizards, cards_json, captured_at)
                VALUES ($id, $name, $format, $wizards, $cards, $capturedAt)
                """;
            var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            var name = insert.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
            var format = insert.Parameters.Add("$format", Microsoft.Data.Sqlite.SqliteType.Text);
            var wizards = insert.Parameters.Add("$wizards", Microsoft.Data.Sqlite.SqliteType.Integer);
            var cards = insert.Parameters.Add("$cards", Microsoft.Data.Sqlite.SqliteType.Text);
            insert.Parameters.AddWithValue("$capturedAt", capturedAt.ToUniversalTime().ToString("O"));

            foreach (var deck in decks)
            {
                id.Value = deck.Id;
                name.Value = deck.Name;
                format.Value = deck.Format;
                wizards.Value = deck.IsWizardsDeck ? 1 : 0;
                cards.Value = JsonSerializer.Serialize(new StoredCards(deck.Commander, deck.Companion, deck.Main, deck.Sideboard));
                await insert.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>The stored list, or null when Arena has never been seen logging one.</summary>
    public async Task<ArenaDeckSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT deck_id, name, format, wizards, cards_json, captured_at FROM arena_decks";

        var decks = new List<ArenaDeck>();
        DateTimeOffset? capturedAt = null;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cards = JsonSerializer.Deserialize<StoredCards>(reader.GetString(4)) ?? StoredCards.Empty;
            decks.Add(new ArenaDeck(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1,
                cards.Commander ?? [], cards.Companion ?? [], cards.Main ?? [], cards.Sideboard ?? []));
            capturedAt ??= DateTimeOffset.Parse(reader.GetString(5));
        }

        return capturedAt is { } at ? new ArenaDeckSnapshot(decks, at) : null;
    }

    private sealed record StoredCards(
        IReadOnlyList<ArenaCard>? Commander,
        IReadOnlyList<ArenaCard>? Companion,
        IReadOnlyList<ArenaCard>? Main,
        IReadOnlyList<ArenaCard>? Sideboard)
    {
        public static readonly StoredCards Empty = new([], [], [], []);
    }
}
