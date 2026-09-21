# MTGA Collection Advisor

Ranks MTG Arena decks by **how few wildcards you need to finish them**, based on the cards
you actually own. Standard and Pioneer (Explorer).

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows.

```bash
dotnet run --project src/MtgaCollectionAdvisor.Web
```

It opens in a browser window and stores data in
`%LOCALAPPDATA%\MtgaCollectionAdvisor\advisor.db`. No database server, no setup.

Then, in the app:

1. **Update cards** — downloads the Scryfall card database (once, ~30s).
2. **Capture collection** — with MTG Arena open. Also runs automatically when the game starts.
3. **Fetch decks** — pulls public lists for the selected format.

Standalone build:

```bash
dotnet publish src/MtgaCollectionAdvisor.Web -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## How the collection is read

The app reads the memory of the running `MTGA.exe` process.

The current Arena client doesn't write your collection anywhere else — not to `Player.log`
(where older trackers used to find it), not to a cache file, not to the registry. Wildcard
totals are still logged and read from there.

The read is one-way: the process is opened with `PROCESS_VM_READ`, nothing is written back,
nothing is injected, no game file is touched. Administrator rights are not needed.

If a client update breaks it, you can paste a collection list by hand or import the
`mtga_collection.json` from
[MTGA-collection-exporter](https://github.com/NthPhantom10/MTGA-collection-exporter), whose
approach this one is adapted from.

## Limitations

- Windows only.
- Archidekt lets anyone file any list under any format, so decks that aren't actually legal
  get filtered out — expect a chunk of each fetch to disappear.
- Only Standard and Pioneer. Arena has no Pioneer queue; those decks are playable in Explorer.

## Credits

Card data from [Scryfall](https://scryfall.com/docs/api) · decklists from
[Archidekt](https://archidekt.com) · mana symbols from
[mana-font](https://github.com/andrewgioia/mana-font) · memory scanning adapted from
[MTGA-collection-exporter](https://github.com/NthPhantom10/MTGA-collection-exporter) (MIT).

Unofficial fan project, not affiliated with Wizards of the Coast.
