# MTGA Deck Advisor

*Find the decks your collection can build — and what the rest will cost.*

Ranks MTG Arena decks by **how few wildcards you need to finish them**, based on the cards
you actually own. Standard and Pioneer.

## Install

Windows only. Download `MtgaDeckAdvisor-win-Setup.exe` from the
[latest release](https://github.com/Dasayeve/MtgaCollectionAdvisor/releases/latest) and run it.
It installs for your user (no administrator rights) and adds a desktop and Start menu shortcut.
No .NET install is needed.

**Windows SmartScreen will warn about it.** The installer isn't code-signed yet, so Windows
doesn't recognise it: choose **More info → Run anyway**. To check that the file is the one
published here, compare its hash with `SHA256SUMS.txt` in the same release:

```powershell
Get-FileHash .\MtgaDeckAdvisor-win-Setup.exe -Algorithm SHA256
```

**Updates install themselves.** The app checks for a new version when it starts and downloads
it in the background. Click **Restart to update** in the status bar, or just close the app and
it will be on the new version next time.

Your data lives in `%LOCALAPPDATA%\MtgaCollectionAdvisor\advisor.db` and is kept across
updates and uninstalls. Delete that folder too to remove everything.

A portable zip is also attached to each release, if you'd rather not install.

**The first start sets everything up by itself** (a couple of minutes): it downloads the
card database and recent Standard decks, then asks you to open MTG Arena and reads your
collection from it. After that, **Fetch decks** refreshes the decks, **Update cards** the
card database, and the collection is read again whenever MTG Arena starts.

In MTG Arena, turn on **Options → Account → Detailed Logs (Plugin Support)**: your wildcard
totals and saved decks are read from that log.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows.

```bash
dotnet run --project src/MtgaCollectionAdvisor.Web
```

It opens in a browser window and stores data in the same `advisor.db`. No database server,
no setup.

Releases are built by `.github/workflows/release.yml` when a `v*` tag is pushed: it runs the
tests, publishes a self-contained build, and packs it with [Velopack](https://velopack.io). How to cut
a new version is in [RELEASING.md](RELEASING.md).

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
- Only Standard and Pioneer.

## Contributing

Issues and pull requests are welcome — feature requests included. If something is missing
or broken, open an issue.

## Credits

The memory-scanning approach comes from
[MTGA-collection-exporter](https://github.com/NthPhantom10/MTGA-collection-exporter) by
**NthPhantom10**, who worked out how to find the collection in the client's memory. This
project is a C# port of that idea — thank you.

Card data from [Scryfall](https://scryfall.com/docs/api) · decklists from
[Archidekt](https://archidekt.com) · mana symbols from
[mana-font](https://github.com/andrewgioia/mana-font).

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for full licences.

## Licence

[MIT](LICENSE). Unofficial fan project, not affiliated with Wizards of the Coast.
