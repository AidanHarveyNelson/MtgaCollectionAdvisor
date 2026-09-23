using System.Globalization;
using MtgaCollectionAdvisor.Core.Storage;

namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>
/// The creator-video cache. Only what a video's deck *is* lives here - it never changes
/// once published. What it *costs* depends on the collection and is always computed fresh.
/// </summary>
public sealed class CreatorVideoStore(Database database)
{
    public async Task<CreatorVideoSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);

        DateTimeOffset? refreshedAt = null;
        await using (var state = connection.CreateCommand())
        {
            state.CommandText = "SELECT refreshed_at FROM creator_feed_state WHERE id = 1";
            if (await state.ExecuteScalarAsync(ct) is string text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                refreshedAt = at;
            }
        }

        var videos = new List<CreatorVideo>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT video_id, creator, title, published_at, source_kind,
                       decklist, archidekt_id, external_site, external_url, language
                FROM creator_videos
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                videos.Add(new CreatorVideo(
                    VideoId: reader.GetString(0),
                    Creator: reader.GetString(1),
                    Title: reader.GetString(2),
                    Published: DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    // An unknown kind (a newer build's) degrades to "no deck", never a crash.
                    Kind: Enum.TryParse<DeckSourceKind>(reader.GetString(4), out var kind) ? kind : DeckSourceKind.None,
                    Decklist: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ArchidektId: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    ExternalSite: reader.IsDBNull(7) ? null : reader.GetString(7),
                    ExternalUrl: reader.IsDBNull(8) ? null : reader.GetString(8),
                    Language: reader.GetString(9)));
            }
        }

        return new CreatorVideoSnapshot(refreshedAt, videos);
    }

    /// <summary>Replaces every row and the refresh time together, so a failed save leaves the old cache whole.</summary>
    public async Task ReplaceAsync(CreatorVideoSnapshot snapshot, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM creator_videos";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO creator_videos (video_id, creator, title, published_at, source_kind,
                                            decklist, archidekt_id, external_site, external_url, language)
                VALUES ($id, $creator, $title, $published, $kind, $decklist, $archidekt, $site, $url, $language)
                ON CONFLICT (video_id) DO NOTHING
                """;
            foreach (var video in snapshot.Videos)
            {
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$id", video.VideoId);
                insert.Parameters.AddWithValue("$creator", video.Creator);
                insert.Parameters.AddWithValue("$title", video.Title);
                insert.Parameters.AddWithValue("$published", video.Published.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$kind", video.Kind.ToString());
                insert.Parameters.AddWithValue("$decklist", (object?)video.Decklist ?? DBNull.Value);
                insert.Parameters.AddWithValue("$archidekt", (object?)video.ArchidektId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$site", (object?)video.ExternalSite ?? DBNull.Value);
                insert.Parameters.AddWithValue("$url", (object?)video.ExternalUrl ?? DBNull.Value);
                insert.Parameters.AddWithValue("$language", video.Language);
                await insert.ExecuteNonQueryAsync(ct);
            }
        }

        await using (var state = connection.CreateCommand())
        {
            if (snapshot.RefreshedAt is { } at)
            {
                state.CommandText = """
                    INSERT INTO creator_feed_state (id, refreshed_at) VALUES (1, $at)
                    ON CONFLICT (id) DO UPDATE SET refreshed_at = excluded.refreshed_at
                    """;
                state.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
            }
            else
            {
                state.CommandText = "DELETE FROM creator_feed_state";
            }
            await state.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
