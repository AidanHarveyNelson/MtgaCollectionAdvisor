using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Runs against a throwaway SQLite file: the query itself (prefix matching, escaping,
/// limit) is the logic worth testing, and it only exists in SQL.
/// </summary>
public sealed class CardNameSearchTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-test-{Guid.NewGuid():N}.db");
    private CardDatabaseStore _store = null!;

    public async Task InitializeAsync()
    {
        var database = new Database(_databasePath);
        await SchemaInitializer.EnsureCreatedAsync(database);
        _store = new CardDatabaseStore(database);

        await _store.ReplaceAllAsync(Cards());
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchNamesAsync_Should_ReturnEmpty_When_TermIsShorterThanTwoChars()
    {
        Assert.Empty(await _store.SearchNamesAsync("L"));
        Assert.Empty(await _store.SearchNamesAsync(" "));
        Assert.Empty(await _store.SearchNamesAsync(""));
    }

    [Fact]
    public async Task SearchNamesAsync_Should_MatchByPrefix_IgnoringCase()
    {
        var results = await _store.SearchNamesAsync("light");

        Assert.Contains("Lightning Bolt", results);
        Assert.Contains("Lightning Strike", results);
        Assert.DoesNotContain("Shock", results);
    }

    [Fact]
    public async Task SearchNamesAsync_Should_TreatWildcardCharactersLiterally()
    {
        // Without escaping, "%" would match every card in the database.
        Assert.Empty(await _store.SearchNamesAsync("%a"));
    }

    [Fact]
    public async Task SearchNamesAsync_Should_RespectTheLimit()
    {
        var results = await _store.SearchNamesAsync("li", limit: 1);

        Assert.Single(results);
    }

    private static async IAsyncEnumerable<CardInfo> Cards()
    {
        yield return Card(1, "Lightning Bolt");
        yield return Card(2, "Lightning Strike");
        yield return Card(3, "Shock");
        await Task.CompletedTask;
    }

    private static CardInfo Card(int grpId, string name) =>
        new(grpId, name, "TST", "{R}", "R", CardRarity.Common, StandardLegal: true, PioneerLegal: true);
}
