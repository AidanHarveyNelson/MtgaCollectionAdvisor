namespace MtgaCollectionAdvisor.Core.Storage;

public static class SchemaInitializer
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS collection_cards (
            grp_id     INTEGER PRIMARY KEY,
            quantity   INTEGER NOT NULL,
            synced_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS wildcard_inventory (
            id         INTEGER PRIMARY KEY CHECK (id = 1),
            commons    INTEGER NOT NULL,
            uncommons  INTEGER NOT NULL,
            rares      INTEGER NOT NULL,
            mythics    INTEGER NOT NULL,
            synced_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS cards (
            grp_id           INTEGER PRIMARY KEY,
            name             TEXT NOT NULL,
            set_code         TEXT NOT NULL,
            mana_cost        TEXT NOT NULL,
            colors           TEXT NOT NULL,
            rarity           TEXT NOT NULL,
            standard_legal   INTEGER NOT NULL,
            pioneer_legal    INTEGER NOT NULL,
            updated_at       TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_cards_name ON cards (name COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS card_import_state (
            id             INTEGER PRIMARY KEY CHECK (id = 1),
            last_imported  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS decks (
            source_id    TEXT PRIMARY KEY,
            format_key   TEXT NOT NULL,
            name         TEXT NOT NULL,
            url          TEXT NOT NULL,
            popularity   INTEGER NOT NULL,
            fetched_at   TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_decks_format ON decks (format_key);

        -- No foreign key to decks on purpose: a pin has to outlive the deck row being
        -- deleted and reinserted by a source refresh, which is the whole point of pinning.
        CREATE TABLE IF NOT EXISTS pinned_decks (
            source_id             TEXT PRIMARY KEY,
            format_key            TEXT NOT NULL,
            pinned_at             TEXT NOT NULL,
            wildcards_when_pinned INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS deck_cards (
            source_id  TEXT NOT NULL REFERENCES decks (source_id) ON DELETE CASCADE,
            card_name  TEXT NOT NULL,
            board      TEXT NOT NULL,
            quantity   INTEGER NOT NULL,
            PRIMARY KEY (source_id, card_name, board)
        );

        -- Creator videos: what each video's description told us, so neither its feed nor
        -- Archidekt is asked again. The description itself is not kept.
        CREATE TABLE IF NOT EXISTS creator_videos (
            video_id       TEXT PRIMARY KEY,
            creator        TEXT NOT NULL,
            title          TEXT NOT NULL,
            published_at   TEXT NOT NULL,
            source_kind    TEXT NOT NULL,
            decklist       TEXT,
            archidekt_id   INTEGER,
            external_site  TEXT,
            external_url   TEXT,
            language       TEXT NOT NULL DEFAULT 'en'
        );

        -- Per channel, so each feed keeps its own schedule and backoff (CreatorFeedSchedule).
        CREATE TABLE IF NOT EXISTS creator_feeds (
            creator               TEXT PRIMARY KEY,
            last_success_at       TEXT,
            last_attempt_at       TEXT,
            consecutive_failures  INTEGER NOT NULL
        );
        """;

    public static async Task EnsureCreatedAsync(Database database, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
