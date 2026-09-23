using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Runs against a throwaway SQLite file: the query itself (prefix matching, literal
/// wildcards, limit, index use) is the logic worth testing, and it only exists in SQL.
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
    public async Task SearchNamesAsync_Should_TreatUnderscoreLiterally()
    {
        // "_" is LIKE's single-character wildcard; unescaped it would match "Lightning".
        Assert.Empty(await _store.SearchNamesAsync("Lightn_ng"));
    }

    [Fact]
    public async Task SearchNamesAsync_Should_MatchByPrefix_When_TermIsUpperCase()
    {
        Assert.Contains("Lightning Bolt", await _store.SearchNamesAsync("LIGHTNING"));
    }

    [Fact]
    public async Task SearchNamesAsync_Should_MatchNames_When_TheyContinueWithNonAsciiCharacters()
    {
        // The range's upper bound has to sort after accented letters, not just ASCII.
        Assert.Contains("Lim-Dûl's Vault", await _store.SearchNamesAsync("lim-d"));
    }

    [Fact]
    public async Task SearchNamesAsync_Should_UseTheNameIndex()
    {
        var plan = await QueryPlanAsync(CardDatabaseStore.SearchNamesSql,
            ("$from", "light"), ("$to", "light􏿿"), ("$limit", "10"));

        Assert.DoesNotContain("SCAN", plan);
        Assert.Contains("SEARCH cards USING COVERING INDEX ix_cards_name", plan);
    }

    /// <summary>
    /// The plan SQLite picks for the store's own SQL. A lookup that scans the card table
    /// still returns the right rows - only ~200x slower - so nothing but the plan shows it.
    /// </summary>
    private async Task<string> QueryPlanAsync(string sql, params (string Name, string Value)[] parameters)
    {
        await using var connection = await new Database(_databasePath).OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        var steps = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) steps.Add(reader.GetString(3));
        return string.Join(" | ", steps);
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
        yield return Card(4, "Lim-Dûl's Vault");
        await Task.CompletedTask;
    }

    private static CardInfo Card(int grpId, string name) =>
        new(grpId, name, "TST", "{R}", "R", CardRarity.Common, StandardLegal: true, PioneerLegal: true);
}
