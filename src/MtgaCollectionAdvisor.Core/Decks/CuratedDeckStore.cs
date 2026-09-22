using Microsoft.Data.Sqlite;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Decks;

/// <summary>
/// Stores the pool of candidate decks - both the ones fetched automatically and the
/// ones the user pasted in by hand. Auto-fetched decks are replaced wholesale on each
/// refresh; manual ones are only ever added or deleted individually.
/// </summary>
public sealed class CuratedDeckStore(Database database)
{
    /// <summary>
    /// Replaces every previously auto-fetched deck for a format (source ids starting
    /// with <paramref name="sourcePrefix"/>, e.g. "archidekt:") with a fresh batch,
    /// leaving manually-imported decks for that format untouched.
    /// </summary>
    public async Task ReplaceAutoFetchedAsync(
        FormatDefinition format, string sourcePrefix, IReadOnlyList<CandidateDeck> decks, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = connection.CreateCommand())
        {
            // Pinned decks are spared: the user is tracking them, and a deck dropped by the
            // source would otherwise vanish mid-progress.
            clear.CommandText = """
                DELETE FROM decks
                WHERE format_key = $format AND source_id LIKE $prefix
                  AND source_id NOT IN (SELECT source_id FROM pinned_decks)
                """;
            clear.Parameters.AddWithValue("$format", format.Key);
            clear.Parameters.AddWithValue("$prefix", sourcePrefix + "%");
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var deck in decks)
        {
            // A spared pinned deck is still in this batch, so replace it rather than
            // colliding on the primary key - its contents should track the source.
            await using (var replace = connection.CreateCommand())
            {
                replace.CommandText = "DELETE FROM decks WHERE source_id = $id";
                replace.Parameters.AddWithValue("$id", deck.SourceId);
                await replace.ExecuteNonQueryAsync(ct);
            }

            await InsertDeckAsync(connection, deck, ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task AddDeckAsync(CandidateDeck deck, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await InsertDeckAsync(connection, deck, ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task InsertDeckAsync(SqliteConnection connection, CandidateDeck deck, CancellationToken ct)
    {
        await using (var insertDeck = connection.CreateCommand())
        {
            insertDeck.CommandText = """
                INSERT INTO decks (source_id, format_key, name, url, popularity, fetched_at)
                VALUES ($id, $format, $name, $url, $popularity, $fetchedAt)
                """;
            insertDeck.Parameters.AddWithValue("$id", deck.SourceId);
            insertDeck.Parameters.AddWithValue("$format", deck.FormatKey);
            insertDeck.Parameters.AddWithValue("$name", deck.Name);
            insertDeck.Parameters.AddWithValue("$url", deck.Url);
            insertDeck.Parameters.AddWithValue("$popularity", deck.Popularity);
            insertDeck.Parameters.AddWithValue("$fetchedAt", deck.FetchedAt.ToString("O"));
            await insertDeck.ExecuteNonQueryAsync(ct);
        }

        await InsertCardsAsync(connection, deck, ct);
    }

    private static async Task InsertCardsAsync(SqliteConnection connection, CandidateDeck deck, CancellationToken ct)
    {
        // Deck sources list cards per printing, so the same card name can appear more
        // than once on a board ("7 Island" as two entries) - collapse those first.
        var merged = deck.Cards
            .GroupBy(c => (c.Name, c.Board))
            .Select(g => new DeckCardRef(g.Key.Name, g.Sum(c => c.Quantity), g.Key.Board));

        await using var insertCard = connection.CreateCommand();
        insertCard.CommandText = """
            INSERT INTO deck_cards (source_id, card_name, board, quantity)
            VALUES ($id, $name, $board, $qty)
            """;
        insertCard.Parameters.AddWithValue("$id", deck.SourceId);
        var nameParameter = insertCard.Parameters.Add("$name", SqliteType.Text);
        var boardParameter = insertCard.Parameters.Add("$board", SqliteType.Text);
        var quantityParameter = insertCard.Parameters.Add("$qty", SqliteType.Integer);

        foreach (var card in merged)
        {
            nameParameter.Value = card.Name;
            boardParameter.Value = card.Board.ToString();
            quantityParameter.Value = card.Quantity;
            await insertCard.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Replaces a user deck's name, format and cards while keeping its source id, so the
    /// pin on it survives - re-importing mints a new id and takes the pin's baseline with
    /// it, which is the whole reason this exists.
    ///
    /// Refuses anything but a manual deck: an auto-fetched one is overwritten wholesale on
    /// the next refresh, so the edit would vanish without a word.
    /// </summary>
    public async Task UpdateUserDeckAsync(CandidateDeck deck, CancellationToken ct = default)
    {
        if (!deck.IsUserDeck)
        {
            throw new InvalidOperationException(
                $"Only user decks can be edited; '{deck.SourceId}' is fetched and would be overwritten on the next refresh.");
        }

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE decks SET format_key = $format, name = $name WHERE source_id = $id
                """;
            update.Parameters.AddWithValue("$id", deck.SourceId);
            update.Parameters.AddWithValue("$format", deck.FormatKey);
            update.Parameters.AddWithValue("$name", deck.Name);

            // Nothing updated means the deck is gone - saying nothing here would report
            // success for a write that never happened.
            if (await update.ExecuteNonQueryAsync(ct) == 0)
            {
                throw new InvalidOperationException($"Deck '{deck.SourceId}' no longer exists.");
            }
        }

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM deck_cards WHERE source_id = $id";
            clear.Parameters.AddWithValue("$id", deck.SourceId);
            await clear.ExecuteNonQueryAsync(ct);
        }

        await InsertCardsAsync(connection, deck, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteDeckAsync(string sourceId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM decks WHERE source_id = $id";
        command.Parameters.AddWithValue("$id", sourceId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CandidateDeck>> LoadAsync(FormatDefinition format, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);

        var decksById = new Dictionary<string, (string Name, string Url, int Popularity, DateTimeOffset FetchedAt, List<DeckCardRef> Cards)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT source_id, name, url, popularity, fetched_at FROM decks WHERE format_key = $format";
            command.Parameters.AddWithValue("$format", format.Key);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                DateTimeOffset.TryParse(reader.GetString(4), out var fetchedAt);
                decksById[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2), reader.GetInt32(3), fetchedAt, []);
            }
        }

        if (decksById.Count == 0) return [];

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT dc.source_id, dc.card_name, dc.board, dc.quantity
                FROM deck_cards dc
                JOIN decks d ON d.source_id = dc.source_id
                WHERE d.format_key = $format
                """;
            command.Parameters.AddWithValue("$format", format.Key);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!decksById.TryGetValue(reader.GetString(0), out var deck)) continue;
                var board = Enum.Parse<DeckBoard>(reader.GetString(2));
                deck.Cards.Add(new DeckCardRef(reader.GetString(1), reader.GetInt32(3), board));
            }
        }

        return decksById
            .Select(kv => new CandidateDeck(kv.Key, kv.Value.Name, kv.Value.Url, format.Key, kv.Value.Popularity, kv.Value.Cards, kv.Value.FetchedAt))
            .ToList();
    }
}
