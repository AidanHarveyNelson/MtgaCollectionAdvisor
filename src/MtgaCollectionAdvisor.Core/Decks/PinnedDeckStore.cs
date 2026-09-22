using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Decks;

/// <summary>
/// A deck the user is working towards, with the wildcard gap recorded at the moment it was
/// pinned so progress can be measured against a fixed baseline.
/// </summary>
public sealed record PinnedDeck(
    string SourceId,
    string FormatKey,
    DateTimeOffset PinnedAt,
    int WildcardsWhenPinned);

public sealed class PinnedDeckStore(Database database)
{
    /// <summary>
    /// Pinning an already-pinned deck is a no-op: rewriting the baseline would silently
    /// erase the progress the user is tracking.
    /// </summary>
    public async Task PinAsync(string sourceId, string formatKey, int wildcardsNow, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;

        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pinned_decks (source_id, format_key, pinned_at, wildcards_when_pinned)
            VALUES ($id, $format, $at, $wildcards)
            ON CONFLICT (source_id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        command.Parameters.AddWithValue("$format", formatKey);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$wildcards", Math.Max(0, wildcardsNow));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Moves a pin's baseline to a new wildcard count, for when the deck itself changed.
    /// Deliberately separate from <see cref="PinAsync"/>, which refuses to overwrite a
    /// baseline because that is how progress is preserved - here the list the baseline
    /// referred to no longer exists, so keeping it would report progress never made.
    ///
    /// Does nothing if the deck is not pinned: rebasing must never create a pin.
    /// </summary>
    public async Task RebaseAsync(string sourceId, int wildcardsNow, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;

        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pinned_decks SET wildcards_when_pinned = $wildcards, pinned_at = $at
            WHERE source_id = $id
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$wildcards", Math.Max(0, wildcardsNow));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UnpinAsync(string sourceId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pinned_decks WHERE source_id = $id";
        command.Parameters.AddWithValue("$id", sourceId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, PinnedDeck>> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_id, format_key, pinned_at, wildcards_when_pinned FROM pinned_decks";

        var pins = new Dictionary<string, PinnedDeck>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var sourceId = reader.GetString(0);
            DateTimeOffset.TryParse(reader.GetString(2), out var pinnedAt);
            pins[sourceId] = new PinnedDeck(sourceId, reader.GetString(1), pinnedAt, reader.GetInt32(3));
        }
        return pins;
    }
}
