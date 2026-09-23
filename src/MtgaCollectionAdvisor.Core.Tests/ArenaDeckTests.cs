using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using MtgaCollectionAdvisor.Core.Arena;
using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Export;
using MtgaCollectionAdvisor.Core.Logs;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// The decks saved inside MTG Arena, read from the login message in Player.log and
/// written back out in Arena's own format.
/// </summary>
public sealed class ArenaDeckTests : IAsyncLifetime
{
    // Trimmed from a real StartHook payload (2026-09-23): same keys, same nesting, a few decks.
    private const string StartHook = """
        {
          "InventoryInfo": { "WildCardCommons": 120, "WildCardUnCommons": 64, "WildCardRares": 6, "WildCardMythics": 11 },
          "DeckSummaries": [
            { "DeckIdInternal": "own-1", "Name": "Golgari pest", "Attributes": [ { "name": "Format", "value": "Standard" } ] },
            { "DeckIdInternal": "own-2", "Name": "Brawl deck", "Attributes": [ { "name": "Format", "value": "HistoricBrawl" } ] },
            { "DeckIdInternal": "net-1", "Name": "Suggested", "IsNetDeck": true, "Attributes": [ { "name": "Format", "value": "Standard" } ] },
            { "DeckIdInternal": "pre-1", "Name": "?=?Loc/Decks/Precon/CC_ANB_W", "Attributes": [ { "name": "Format", "value": "Alchemy" } ] },
            { "DeckIdInternal": "missing", "Name": "No contents", "Attributes": [] }
          ],
          "DecksInternal": {
            "own-1": { "MainDeck": [ { "cardId": 100, "quantity": 4 }, { "cardId": 300, "quantity": 20 }, { "cardId": 100, "quantity": 1 } ],
                       "Sideboard": [ { "cardId": 400, "quantity": 2 } ],
                       "Companions": [ { "cardId": 500, "quantity": 1 } ],
                       "CardSkins": [ { "grpId": 100, "ccv": "DA" } ] },
            "own-2": { "MainDeck": [ { "cardId": 300, "quantity": 30 } ], "CommandZone": [ { "cardId": 200, "quantity": 1 } ] },
            "net-1": { "MainDeck": [ { "cardId": 300, "quantity": 24 } ] },
            "pre-1": { "MainDeck": [ { "cardId": 300, "quantity": 24 } ] }
          }
        }
        """;

    private static readonly Dictionary<int, string> Names = new()
    {
        [100] = "Lightning Bolt",
        [200] = "Bonecrusher Giant // Stomp",
        [300] = "Swamp",
        [400] = "Negate",
        [500] = "Lurrus of the Dream-Den",
    };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-arena-{Guid.NewGuid():N}.db");
    private Database _database = null!;

    public async Task InitializeAsync()
    {
        _database = new Database(_databasePath);
        await SchemaInitializer.EnsureCreatedAsync(_database);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public void TryGetArenaDecks_Should_ReadDecksFromStartHook()
    {
        Assert.True(LogEventInterpreter.TryGetArenaDecks(new LogEvent("StartHook", StartHook), out var decks));

        Assert.Equal(["own-1", "own-2", "net-1", "pre-1"], decks.Select(d => d.Id));

        var pest = decks[0];
        Assert.Equal("Golgari pest", pest.Name);
        Assert.Equal("Standard", pest.Format);
        Assert.Contains(new ArenaCard(100, 5), pest.Main);   // listed twice, summed
        Assert.Equal([new ArenaCard(400, 2)], pest.Sideboard);
        Assert.Equal([new ArenaCard(500, 1)], pest.Companion);

        Assert.Equal([new ArenaCard(200, 1)], decks[1].Commander);
    }

    [Fact]
    public void TryGetArenaDecks_Should_MarkWizardsDecks()
    {
        LogEventInterpreter.TryGetArenaDecks(new LogEvent("StartHook", StartHook), out var decks);

        Assert.Equal([false, false, true, true], decks.Select(d => d.IsWizardsDeck));
        Assert.Equal("CC_ANB_W", decks[3].Name);
    }

    [Fact]
    public void TryGetArenaDecks_Should_IgnoreOtherMessages()
    {
        var inventoryOnly = """{ "InventoryInfo": { "WildCardCommons": 1 } }""";

        Assert.False(LogEventInterpreter.TryGetArenaDecks(new LogEvent("StartHook", inventoryOnly), out _));
        Assert.False(LogEventInterpreter.TryGetArenaDecks(new LogEvent("x", "not json"), out _));
    }

    [Fact]
    public void TryGetInventoryInfo_Should_StillReadWildcards_FromTheSameMessage()
    {
        Assert.True(LogEventInterpreter.TryGetInventoryInfo(new LogEvent("StartHook", StartHook), out var inventory));
        Assert.Equal(new WildcardInventory(120, 64, 6, 11), inventory);
    }

    [Fact]
    public void Write_Should_WriteArenaSections()
    {
        var text = ArenaDeckTextWriter.Write(Deck(
            commander: [new(200, 1)], companion: [new(500, 1)], main: [new(300, 20)], sideboard: [new(400, 2)]), Names);

        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["Commander", "1 Bonecrusher Giant", "Companion", "1 Lurrus of the Dream-Den",
                      "Deck", "20 Swamp", "Sideboard", "2 Negate"], lines);
    }

    [Fact]
    public void Write_Should_NoteCardsMissingFromTheCardDatabase()
    {
        var text = ArenaDeckTextWriter.Write(Deck(main: [new(300, 20), new(999, 3)]), Names);

        Assert.StartsWith("// 3 cards not in the card database", text);
        Assert.Equal([new DeckCardRef("Swamp", 20, DeckBoard.Main)], ArenaDeckListParser.Parse(text));
    }

    [Fact]
    public void Write_Should_RoundTrip_ThroughArenaDeckListParser()
    {
        var text = ArenaDeckTextWriter.Write(Deck(main: [new(100, 4), new(300, 20)], sideboard: [new(400, 2)]), Names);

        Assert.Equal([
            new DeckCardRef("Lightning Bolt", 4, DeckBoard.Main),
            new DeckCardRef("Swamp", 20, DeckBoard.Main),
            new DeckCardRef("Negate", 2, DeckBoard.Sideboard),
        ], ArenaDeckListParser.Parse(text));
    }

    [Fact]
    public async Task ArenaDecks_Should_ExportOwnDecksByDefault_AndWizardsDecksOnRequest()
    {
        LogEventInterpreter.TryGetArenaDecks(new LogEvent("StartHook", StartHook), out var decks);
        var store = new ArenaDeckStore(_database);
        await store.ReplaceAsync(decks, DateTimeOffset.UtcNow);
        var service = new DataExportService(
            new CollectionStore(_database), new CardDatabaseStore(_database), new CuratedDeckStore(_database), store);

        var own = await service.ArenaDecksAsync(includeWizards: false);
        var all = await service.ArenaDecksAsync(includeWizards: true);

        Assert.Equal(["historicbrawl/Brawl deck.txt", "standard/Golgari pest.txt"], EntryNames(own!.Content));
        Assert.Equal(["alchemy/CC_ANB_W.txt", "historicbrawl/Brawl deck.txt", "standard/Golgari pest.txt", "standard/Suggested.txt"],
            EntryNames(all!.Content));
    }

    [Fact]
    public async Task ArenaDecks_Should_BeNull_BeforeArenaWasEverSeen()
    {
        var service = new DataExportService(
            new CollectionStore(_database), new CardDatabaseStore(_database), new CuratedDeckStore(_database), new ArenaDeckStore(_database));

        Assert.Null(await service.ArenaDecksAsync(includeWizards: false));
    }

    [Fact]
    public async Task Store_Should_ReplaceTheWholeList()
    {
        var store = new ArenaDeckStore(_database);
        var capturedAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        await store.ReplaceAsync([Deck("a", main: [new(300, 20)]), Deck("b", main: [new(300, 20)])], capturedAt.AddDays(-1));

        await store.ReplaceAsync([Deck("b", main: [new(100, 4)], sideboard: [new(400, 2)])], capturedAt);

        var snapshot = await store.LoadAsync();
        var deck = Assert.Single(snapshot!.Decks);
        Assert.Equal("b", deck.Id);
        Assert.Equal([new ArenaCard(100, 4)], deck.Main);
        Assert.Equal([new ArenaCard(400, 2)], deck.Sideboard);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }

    [Theory]
    [InlineData("Standard", "standard")]
    [InlineData("HistoricBrawl", "historicbrawl")]
    [InlineData("Unspecified", "unspecified")]
    [InlineData("", "unspecified")]
    public void FolderFor_Should_UseTheLowercasedFormat(string format, string expected)
    {
        Assert.Equal(expected, ArenaDeckTextWriter.FolderFor(format));
    }

    private static ArenaDeck Deck(
        string id = "deck",
        IReadOnlyList<ArenaCard>? commander = null,
        IReadOnlyList<ArenaCard>? companion = null,
        IReadOnlyList<ArenaCard>? main = null,
        IReadOnlyList<ArenaCard>? sideboard = null) =>
        new(id, id, "Standard", false, commander ?? [], companion ?? [], main ?? [], sideboard ?? []);

    private static IReadOnlyList<string> EntryNames(byte[] zipContent)
    {
        using var zip = new ZipArchive(new MemoryStream(zipContent), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
    }
}
