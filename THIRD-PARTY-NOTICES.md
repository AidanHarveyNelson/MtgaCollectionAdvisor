# Third-party notices

## MTGA-collection-exporter

The memory-scanning approach used to read the player's collection
(`src/MtgaCollectionAdvisor.Core/Memory/`) is adapted from
[MTGA-collection-exporter](https://github.com/NthPhantom10/MTGA-collection-exporter) by
**NthPhantom10**, which worked out that the collection can be located in the client's
memory by decoding tables of `(grpId, quantity)` pairs, and how to tell a real collection
apart from other integer tables. That project solved the hard part first; this one is a C#
port of the same idea. Thank you.

```
MIT License

Copyright (c) 2026 NthPhantom10

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## mana-font

Mana symbols are rendered with [mana-font](https://github.com/andrewgioia/mana-font) by
Andrew Gioia, loaded from a CDN. The font's code is MIT licensed; the symbols themselves
are property of Wizards of the Coast.

## Data sources

- [Scryfall](https://scryfall.com/docs/api) — card data, via their public bulk data files.
- [Archidekt](https://archidekt.com) — public decklists, via their public API.

Neither is bundled with this project; both are fetched at runtime.

## Magic: The Gathering

Magic: The Gathering is a trademark of Wizards of the Coast LLC. This is an unofficial fan
project, not affiliated with, endorsed by, or sponsored by Wizards of the Coast.
