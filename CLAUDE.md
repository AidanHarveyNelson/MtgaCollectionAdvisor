# CLAUDE.md

Working notes for this repository. Constraints here were learned the hard way — each one
cost real debugging time, and none of them are obvious from reading the code.

## Layout

| Project | Contains |
|---|---|
| `MtgaCollectionAdvisor.Core` | All logic: memory scanner, Scryfall import, Archidekt client, wildcard analysis, SQLite storage |
| `MtgaCollectionAdvisor.Web` | Blazor Server UI — the only front-end |
| `MtgaCollectionAdvisor.Core.Tests` | xUnit tests |

`Core` has no reference to `Web`. The UI holds no domain logic.

## Collection capture

**The MTGA client does not write the collection anywhere readable.** Not to `Player.log`,
not to a cache file, not to the registry — verified across a full restart, a login to the
main menu, and opening the in-game Collection screen. Do not spend time looking again.
Reading process memory (`Core/Memory/`) is the only route. Wildcard totals *are* still in
`Player.log` and are read from there.

**The decks saved in Arena *are* in `Player.log`**, unlike the collection. They come in the
`StartHook` login message, which is also the one carrying wildcard totals: `DeckSummaries`
(name, `Format` attribute, `IsNetDeck`) and `DecksInternal` (cards by grpId per section,
including `CommandZone` and `Companions`). About half the list is Wizards' decks: suggested
decks have `IsNetDeck`, and precons have `?=?Loc/...` names. Arena writes the list only at
login, so it is stored (`arena_decks`) rather than re-read. Two traps when turning them into app decks:
Arena names Pioneer **Explorer** (the two are unified; decks may carry either name), and it lists a companion in `Companions` *and* in
`Sideboard`, so read the sideboard only (`ArenaDeckImport`).

Two traps in the scanner, both fixed and both easy to reintroduce:

- **Chunked reads must overlap.** A collection table straddling a chunk boundary gets split,
  and only its larger half survives.
- **The client keeps partial views of the collection in memory** (a filtered page, a
  format-restricted pool) that score as well as the real table. The collection is the
  *maximal* such table, so any block essentially contained in a larger one is a view of it.

A real collection has thousands of entries, ~98% known Arena ids, a spread of 1–4 copies,
and cannot average more than 4 copies per card. A block where every quantity is exactly 1
is a UI list, not a collection.

## Blazor

**Interactive components need `@rendermode="InteractiveServer"`** on `<Routes />` and
`<HeadOutlet />` in `App.razor`. Without it the app renders as static HTML: the page looks
perfect, no button responds, and *no error appears anywhere* — not in the browser console,
not in the server log. If nothing is clickable, check this first.

The taskbar icon of the Chromium `--app` window comes from the page's favicon and web app
manifest, not from the executable's embedded icon.

**Closing the window stops the app, 45 s later** (#34). `WindowPresence` counts window
*connections*, not circuits: Blazor keeps a closed window's circuit for about 3 minutes in
case it reconnects, so `OnCircuitClosedAsync` fires far too late. Nothing stops until a
first window has connected, so a `--no-browser` run that nobody opens stays up. A second
launch finds the running instance through `/instance` and opens a window on it. An
instance from before #34, or one killed mid-shutdown, can still hold port 5199 and lock
`bin/` DLLs, so check `tasklist` before blaming a port conflict or a broken build.

**Test on another port while the user has the app open:** set `MTGA_ADVISOR_PORT` (e.g.
5299) for the test run. On 5199, a copy started for testing is where the user's desktop
shortcut opens its window, and stopping it breaks their session mid-use with nothing
saying why. Both copies share `advisor.db`, so a test still writes the user's data.

**Verify with `dotnet run`, not by launching the `.exe` in `bin/`.** Run that way, outside
a publish, the app is in Production and serves no `wwwroot`: every page arrives with no CSS,
and no error.

**Report progress synchronously when a final status follows.** `Progress<T>` posts each
report to run later, so the last "Reading deck 150…" can land after the summary line and
overwrite it in the status bar. `AdvisorSession` has an `ImmediateProgress` for this.

**File downloads are plain `GET` endpoints linked with `<a download>`** (see `/export/*`
in `Program.cs`). Blazor leaves an anchor with a `download` attribute to the browser; the
Chromium `--app` window saves it to Downloads. No JS interop, no blob.

## Testing

Put logic where it can be tested without a UI or a database. Deck-list filtering lives in
`Core/Analysis/DeckFilter.cs` rather than in the Razor component for exactly this reason —
the component just builds a `DeckFilterCriteria` and calls it.

Tests use plain xUnit `Assert`; there is no mocking library, because nothing here needs one.
Tests that genuinely need storage create a throwaway SQLite file and delete it in teardown
(see `CardNameSearchTests`), calling `SqliteConnection.ClearAllPools()` first or the file
stays locked on Windows.

## Database schema

**The schema is versioned: `PRAGMA user_version` is the last migration a database has run**
(#47). `SchemaMigrator` runs `Storage/Migrations.cs` at startup, backs the file up first
(`advisor.db.backup-v{N}`, three kept), commits each migration whole, and refuses a
database newer than the build without touching it. To change the schema, **add a migration
at the end, never edit one**: users' databases have already run it, and a test pins each
migration's hash. Editing `CREATE TABLE IF NOT EXISTS` in place does nothing to an
existing database, which is why this exists.

When a version is released, add `Core.Tests/Fixtures/schema-v{N}.sql` (that version's
schema plus a row of each kind of user data) and list it in the fresh-versus-upgraded test.

`decks` and `deck_cards` are **not** cache: they hold the user's own decks (`manual:` ids)
next to fetched ones (`archidekt:`). A migration may rebuild the cache tables (`cards`,
fetched decks' sync state, creator videos) but must carry user rows across.

## External data

**Escape `LIKE` wildcards when the search term comes from the user.** An unescaped `%`
turns a prefix search into a full-table match.

**But `ESCAPE` switches off SQLite's `LIKE` index optimisation**, so an escaped `LIKE` scans
the whole table: results stay correct, only ~200x slower, with no error. For a prefix
match on an indexed column, write a range instead (`name >= $p AND name < $p || U+10FFFF`,
see `CardDatabaseStore`): it uses the index and has no wildcards to escape. Check any new
lookup with `EXPLAIN QUERY PLAN`. `SCAN` means it will not scale.

**Deck sources send explicit nulls where a list is expected.** `System.Text.Json` writes
those over property initializers, so `= []` on a DTO property does not protect you —
coalesce at the point of use. Archidekt does this for `categories` on untagged cards.

**AetherHub and Moxfield (and MTGGoldfish) refuse automated reads** behind Cloudflare. Do not
try to get past it: open their links for the user and let them paste the export instead.
Archidekt's API is the readable deck source.

**Archidekt's search ignores `pageSize`** (always 60 per page) and stops at 1000 results.
`orderBy=-viewCount` is all-time: its top pages are years-old, rotated decks and never
change. The fetch walks `orderBy=-updatedAt` instead (#36). Standard gets roughly 150
updated decks a *day*, so a walk reaches only a few days back, whatever window the code
sets. Keep to `ArchidektSyncPlanner`'s limits; they are what keeps the app polite.

**YouTube's public channel feeds fail at random (404/500), and throttle a machine that asks
too often** — during #32, bulk probing got every feed refused for hours, for the app too.
Treat a failed feed as "no news", never "no videos", and keep to `CreatorFeedSchedule`. Do
not bulk-probe feeds while testing.

Deck sites let anyone file any list under any format, so fetched decks must be checked for
format legality rather than trusted. Scryfall's bulk data lists a few `arena_id` values more
than once, so dedupe before inserting against a primary key.
