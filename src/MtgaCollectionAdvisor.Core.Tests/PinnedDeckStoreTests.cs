using Microsoft.Data.Sqlite;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public sealed class PinnedDeckStoreTests : IAsyncLifetime
{
    private const string Prefix = "archidekt:";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-pins-{Guid.NewGuid():N}.db");
    private PinnedDeckStore _pins = null!;
    private CuratedDeckStore _decks = null!;

    public async Task InitializeAsync()
    {
        var database = new Database(_databasePath);
        await SchemaInitializer.EnsureCreatedAsync(database);
        _pins = new PinnedDeckStore(database);
        _decks = new CuratedDeckStore(database);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PinnedDeck_Should_SurviveAutoFetchRefresh()
    {
        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:1", "Tracked")]);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 14);

        // The source no longer lists it - the deck must not disappear mid-progress.
        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:2", "Something else")]);

        var stored = await _decks.LoadAsync(Formats.Standard);
        Assert.Contains(stored, d => d.SourceId == "archidekt:1");
        Assert.Contains(stored, d => d.SourceId == "archidekt:2");

        var pins = await _pins.LoadAsync();
        Assert.Equal(14, pins["archidekt:1"].WildcardsWhenPinned);
    }

    [Fact]
    public async Task RefreshedPinnedDeck_Should_GetUpdatedCards()
    {
        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:1", "Tracked", "Mountain")]);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 9);

        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:1", "Tracked", "Island")]);

        var deck = Assert.Single(await _decks.LoadAsync(Formats.Standard));
        Assert.Contains(deck.Cards, c => c.Name == "Island");
        Assert.DoesNotContain(deck.Cards, c => c.Name == "Mountain");
    }

    [Fact]
    public async Task UnpinnedDecks_Should_StillBeReplacedByRefresh()
    {
        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:1", "Disposable")]);

        await _decks.ReplaceAutoFetchedAsync(Formats.Standard, Prefix, [Deck("archidekt:2", "Fresh")]);

        var stored = await _decks.LoadAsync(Formats.Standard);
        Assert.Single(stored);
        Assert.Equal("archidekt:2", stored[0].SourceId);
    }

    [Fact]
    public async Task PinAsync_Should_NotResetBaseline_When_CalledTwice()
    {
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 20);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 3);

        var pins = await _pins.LoadAsync();
        Assert.Equal(20, pins["archidekt:1"].WildcardsWhenPinned);
    }

    [Fact]
    public async Task UnpinAsync_Should_RemoveThePin()
    {
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 5);

        await _pins.UnpinAsync("archidekt:1");

        Assert.Empty(await _pins.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnPinsKeyedBySourceId()
    {
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 5);
        await _pins.PinAsync("manual:abc", Formats.Pioneer.Key, wildcardsNow: 11);

        var pins = await _pins.LoadAsync();

        Assert.Equal(2, pins.Count);
        Assert.Equal(Formats.Pioneer.Key, pins["manual:abc"].FormatKey);
    }

    private static CandidateDeck Deck(string sourceId, string name, string card = "Mountain") => new(
        SourceId: sourceId,
        Name: name,
        Url: "",
        FormatKey: Formats.Standard.Key,
        Popularity: 0,
        Cards: [new DeckCardRef(card, 4, DeckBoard.Main)],
        FetchedAt: DateTimeOffset.UtcNow);
}
