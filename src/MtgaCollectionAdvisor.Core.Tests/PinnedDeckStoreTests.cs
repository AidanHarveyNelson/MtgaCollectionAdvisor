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
        await SchemaMigrator.MigrateAsync(database);
        _pins = new PinnedDeckStore(database);
        _decks = new CuratedDeckStore(database);
    }

    public Task DisposeAsync()
    {
        TestDatabaseFiles.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PinnedDeck_Should_SurvivePrune_When_ItLeavesTheWindow()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", "Tracked", daysAgo: 100)]);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 14);

        // The source no longer lists it as recent - the deck must not disappear mid-progress.
        await _decks.PruneFetchedAsync(Formats.Standard, Prefix, Now.AddDays(-90));

        Assert.Contains(await _decks.LoadAsync(Formats.Standard), d => d.SourceId == "archidekt:1");
        var pins = await _pins.LoadAsync();
        Assert.Equal(14, pins["archidekt:1"].WildcardsWhenPinned);
    }

    [Fact]
    public async Task PinnedDeck_Should_StayFrozen_When_TheSourceChangesIt()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", "Tracked", daysAgo: 5, card: "Mountain")]);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 9);

        var result = await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", "Tracked", daysAgo: 1, card: "Island")]);

        Assert.Equal(new DeckMergeResult(0, 0), result);
        var deck = Assert.Single(await _decks.LoadAsync(Formats.Standard));
        Assert.Contains(deck.Cards, c => c.Name == "Mountain");
        Assert.DoesNotContain(deck.Cards, c => c.Name == "Island");
    }

    [Fact]
    public async Task PinnedDeck_Should_NotBeRemoved_When_ASourceRejectsIt()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", "Tracked", daysAgo: 5)]);
        await _pins.PinAsync("archidekt:1", Formats.Standard.Key, wildcardsNow: 9);

        await _decks.MergeFetchedAsync(Formats.Standard, [new FetchedDeck("archidekt:1", Now, Deck: null)]);
        await _decks.RemoveFetchedAsync(["archidekt:1"]);

        Assert.Single(await _decks.LoadAsync(Formats.Standard));
    }

    [Fact]
    public async Task UnpinnedDecks_Should_BePruned_When_TheyLeaveTheWindow()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [
            Fetched("archidekt:1", "Stale", daysAgo: 100),
            Fetched("archidekt:2", "Fresh", daysAgo: 3),
        ]);

        await _decks.PruneFetchedAsync(Formats.Standard, Prefix, Now.AddDays(-90));

        var stored = Assert.Single(await _decks.LoadAsync(Formats.Standard));
        Assert.Equal("archidekt:2", stored.SourceId);
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

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FetchedDeck Fetched(string sourceId, string name, int daysAgo, string card = "Mountain") => new(
        sourceId,
        Now.AddDays(-daysAgo),
        new CandidateDeck(
            SourceId: sourceId,
            Name: name,
            Url: "",
            FormatKey: Formats.Standard.Key,
            Popularity: 0,
            Cards: [new DeckCardRef(card, 4, DeckBoard.Main)],
            FetchedAt: Now));
}
