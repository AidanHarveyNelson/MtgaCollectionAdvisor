using Microsoft.Data.Sqlite;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// How a deck fetch lands in the pool: merged, not replaced, so a second fetch adds to the
/// first rather than starting over.
/// </summary>
public sealed class DeckSyncMergeTests : IAsyncLifetime
{
    private const string Prefix = "archidekt:";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-sync-{Guid.NewGuid():N}.db");
    private CuratedDeckStore _decks = null!;

    public async Task InitializeAsync()
    {
        var database = new Database(_databasePath);
        await SchemaMigrator.MigrateAsync(database);
        _decks = new CuratedDeckStore(database);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Merge_Should_AddNewDecks_And_KeepEarlierOnes()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 5)]);

        var result = await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:2", daysAgo: 1)]);

        Assert.Equal(new DeckMergeResult(Added: 1, Updated: 0), result);
        Assert.Equal(2, (await _decks.LoadAsync(Formats.Standard)).Count);
    }

    [Fact]
    public async Task Merge_Should_ReplaceCards_When_TheSourceChangedADeck()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 5, card: "Mountain")]);

        var result = await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 1, card: "Island")]);

        Assert.Equal(new DeckMergeResult(Added: 0, Updated: 1), result);
        var deck = Assert.Single(await _decks.LoadAsync(Formats.Standard));
        Assert.Equal("Island", Assert.Single(deck.Cards).Name);
    }

    [Fact]
    public async Task Merge_Should_RecordTheSourceVersion_OfKeptAndRejectedDecks()
    {
        var kept = Fetched("archidekt:1", daysAgo: 5);
        var rejected = new FetchedDeck("archidekt:2", Now.AddDays(-2), Deck: null);

        await _decks.MergeFetchedAsync(Formats.Standard, [kept, rejected]);

        var versions = await _decks.LoadSourceVersionsAsync(Formats.Standard);
        Assert.Equal(kept.SourceUpdatedAt, versions["archidekt:1"]);
        Assert.Equal(rejected.SourceUpdatedAt, versions["archidekt:2"]);
    }

    [Fact]
    public async Task Merge_Should_RemoveTheStoredCopy_When_ADeckIsNowRejected()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 5)]);

        await _decks.MergeFetchedAsync(Formats.Standard, [new FetchedDeck("archidekt:1", Now, Deck: null)]);

        Assert.Empty(await _decks.LoadAsync(Formats.Standard));
    }

    [Fact]
    public async Task Prune_Should_RemoveDecksStoredBeforeVersionsExisted()
    {
        // A deck from the old all-time-views fetch: in the pool, but with no version row.
        await _decks.AddDeckAsync(Deck("archidekt:legacy", "Mountain"));
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 5)]);

        var removed = await _decks.PruneFetchedAsync(Formats.Standard, Prefix, Now.AddDays(-90));

        Assert.Equal(1, removed);
        Assert.Equal("archidekt:1", Assert.Single(await _decks.LoadAsync(Formats.Standard)).SourceId);
    }

    [Fact]
    public async Task Prune_Should_NeverRemove_UserDecks()
    {
        await _decks.AddDeckAsync(Deck($"{CandidateDeck.ManualSourcePrefix}mine", "Forest"));

        await _decks.PruneFetchedAsync(Formats.Standard, Prefix, Now.AddDays(-90));
        await _decks.RemoveFetchedAsync([$"{CandidateDeck.ManualSourcePrefix}mine"]);

        Assert.Single(await _decks.LoadAsync(Formats.Standard));
    }

    [Fact]
    public async Task Prune_Should_ForgetVersionsOutsideTheWindow()
    {
        await _decks.MergeFetchedAsync(Formats.Standard, [Fetched("archidekt:1", daysAgo: 100), Fetched("archidekt:2", daysAgo: 1)]);

        await _decks.PruneFetchedAsync(Formats.Standard, Prefix, Now.AddDays(-90));

        Assert.Equal(["archidekt:2"], (await _decks.LoadSourceVersionsAsync(Formats.Standard)).Keys);
    }

    [Fact]
    public async Task SyncState_Should_RoundTrip()
    {
        Assert.Equal(new DeckSyncState(null, null), await _decks.LoadSyncStateAsync(Formats.Standard));

        var state = new DeckSyncState(Now, Now.AddDays(-2));
        await _decks.SaveSyncStateAsync(Formats.Standard, state);

        Assert.Equal(state, await _decks.LoadSyncStateAsync(Formats.Standard));
        Assert.Equal(new DeckSyncState(null, null), await _decks.LoadSyncStateAsync(Formats.Pioneer));
    }

    private static FetchedDeck Fetched(string sourceId, int daysAgo, string card = "Mountain") =>
        new(sourceId, Now.AddDays(-daysAgo), Deck(sourceId, card));

    private static CandidateDeck Deck(string sourceId, string card) => new(
        SourceId: sourceId,
        Name: sourceId,
        Url: "",
        FormatKey: Formats.Standard.Key,
        Popularity: 0,
        Cards: [new DeckCardRef(card, 4, DeckBoard.Main)],
        FetchedAt: Now);
}
