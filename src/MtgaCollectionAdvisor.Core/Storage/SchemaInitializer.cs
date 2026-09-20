using Npgsql;

namespace MtgaCollectionAdvisor.Core.Storage;

public static class SchemaInitializer
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS collection_cards (
            grp_id     INTEGER PRIMARY KEY,
            quantity   INTEGER NOT NULL,
            synced_at  TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS wildcard_inventory (
            id         SMALLINT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
            commons    INTEGER NOT NULL,
            uncommons  INTEGER NOT NULL,
            rares      INTEGER NOT NULL,
            mythics    INTEGER NOT NULL,
            synced_at  TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS cards (
            grp_id           INTEGER PRIMARY KEY,
            name             TEXT NOT NULL,
            set_code         TEXT NOT NULL,
            mana_cost        TEXT NOT NULL,
            colors           TEXT NOT NULL,
            rarity           TEXT NOT NULL,
            standard_legal   BOOLEAN NOT NULL,
            pioneer_legal    BOOLEAN NOT NULL,
            updated_at       TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_cards_name ON cards (lower(name));

        CREATE TABLE IF NOT EXISTS card_import_state (
            id             SMALLINT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
            last_imported  TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS decks (
            source_id    TEXT PRIMARY KEY,
            format_key   TEXT NOT NULL,
            name         TEXT NOT NULL,
            url          TEXT NOT NULL,
            popularity   INTEGER NOT NULL,
            fetched_at   TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_decks_format ON decks (format_key);

        CREATE TABLE IF NOT EXISTS deck_cards (
            source_id  TEXT NOT NULL REFERENCES decks (source_id) ON DELETE CASCADE,
            card_name  TEXT NOT NULL,
            board      TEXT NOT NULL,
            quantity   INTEGER NOT NULL,
            PRIMARY KEY (source_id, card_name, board)
        );
        """;

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(Sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
