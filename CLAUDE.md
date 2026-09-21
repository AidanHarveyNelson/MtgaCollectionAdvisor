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

## Testing

Put logic where it can be tested without a UI or a database. Deck-list filtering lives in
`Core/Analysis/DeckFilter.cs` rather than in the Razor component for exactly this reason —
the component just builds a `DeckFilterCriteria` and calls it.

Tests use plain xUnit `Assert`; there is no mocking library, because nothing here needs one.
Tests that genuinely need storage create a throwaway SQLite file and delete it in teardown
(see `CardNameSearchTests`), calling `SqliteConnection.ClearAllPools()` first or the file
stays locked on Windows.

## External data

**Escape `LIKE` wildcards when the search term comes from the user.** An unescaped `%`
turns a prefix search into a full-table match.

**Deck sources send explicit nulls where a list is expected.** `System.Text.Json` writes
those over property initializers, so `= []` on a DTO property does not protect you —
coalesce at the point of use. Archidekt does this for `categories` on untagged cards.

Deck sites let anyone file any list under any format, so fetched decks must be checked for
format legality rather than trusted. Scryfall's bulk data lists a few `arena_id` values more
than once, so dedupe before inserting against a primary key.
