using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Models;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Resolving a decklist name to Arena printings. Runs against a throwaway SQLite file
/// because the matching rules live in SQL and nowhere else.
///
/// The case that matters: Arena's export format writes only the front face of a
/// double-faced card, while the card database stores the full "Front // Back" name. A
/// single unresolved name used to cost a deck its whole analysis.
/// </summary>
public sealed class CardNameResolutionTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-test-{Guid.NewGuid():N}.db");
    private CardDatabaseStore _store = null!;

    public async Task InitializeAsync()
    {
        var database = new Database(_databasePath);
        await SchemaMigrator.MigrateAsync(database);
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
    public async Task FindByName_Should_ResolveDoubleFacedCard_When_OnlyFrontFaceIsGiven()
    {
        var printings = await _store.FindByNameAsync("Mosswood Dreadknight");

        var printing = Assert.Single(printings);
        Assert.Equal(86952, printing.GrpId);
        Assert.Equal("Mosswood Dreadknight // Dread Whispers", printing.Name);
    }

    [Fact]
    public async Task FindByName_Should_ResolveCard_When_FullDoubleFacedNameIsGiven()
    {
        var printings = await _store.FindByNameAsync("Mosswood Dreadknight // Dread Whispers");

        var printing = Assert.Single(printings);
        Assert.Equal(86952, printing.GrpId);
    }

    [Fact]
    public async Task FindByName_Should_NotMatchBackFace_When_BackFaceNameIsGiven()
    {
        // Arena never exports the back face, so resolving it is not worth a second index.
        Assert.Empty(await _store.FindByNameAsync("Dread Whispers"));
    }

    [Fact]
    public async Task FindByName_Should_EscapeLikeWildcards_When_NameContainsPercent()
    {
        // Unescaped, "%" would turn the front-face branch into a full-table match and
        // attach an arbitrary printing to the card.
        var printings = await _store.FindByNameAsync("%");

        Assert.Empty(printings);
    }

    [Fact]
    public async Task FindByName_Should_BeCaseInsensitive_When_FrontFaceCasingDiffers()
    {
        var printings = await _store.FindByNameAsync("mosswood dreadknight");

        Assert.Single(printings);
    }

    [Fact]
    public async Task FindByName_Should_ReturnEveryPrinting_When_CardHasSeveral()
    {
        var printings = await _store.FindByNameAsync("Lightning Bolt");

        Assert.Equal(2, printings.Count);
    }

    [Fact]
    public async Task FindByName_Should_MatchOnlyTheFrontFace_When_OtherNamesShareItsStart()
    {
        // The front-face match is a range ending exactly at "Fire //": a card whose name
        // merely starts with the same letters must stay out of it.
        var printings = await _store.FindByNameAsync("Fire");

        var printing = Assert.Single(printings);
        Assert.Equal("Fire // Ice", printing.Name);
    }

    [Fact]
    public async Task FindByName_Should_MatchFrontFace_When_CasingDiffersInsideTheName()
    {
        var printings = await _store.FindByNameAsync("FIRE");

        Assert.Equal("Fire // Ice", Assert.Single(printings).Name);
    }

    [Fact]
    public async Task FindByName_Should_UseTheNameIndex_ForBothBranches()
    {
        var plan = await QueryPlanAsync(CardDatabaseStore.FindByNameSql,
            ("$name", "Fire"), ("$frontFace", "Fire // "), ("$frontFaceEnd", "Fire //!"));

        Assert.DoesNotContain("SCAN", plan);
        // Not a covering index here: the lookup returns every column, so each hit still
        // reads its row. What matters is SEARCH, once for each branch of the UNION.
        Assert.Equal(2, CountOf(plan, "SEARCH cards USING INDEX ix_cards_name"));
    }

    private static int CountOf(string text, string part) =>
        (text.Length - text.Replace(part, "").Length) / part.Length;

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

    private static async IAsyncEnumerable<CardInfo> Cards()
    {
        yield return Card(86952, "Mosswood Dreadknight // Dread Whispers");
        yield return Card(1, "Lightning Bolt");
        yield return Card(2, "Lightning Bolt");
        yield return Card(3, "Shock");
        yield return Card(4, "Fire // Ice");
        yield return Card(5, "Fireball");
        yield return Card(6, "Fire Ants");
        await Task.CompletedTask;
    }

    private static CardInfo Card(int grpId, string name) =>
        new(grpId, name, "TST", "{R}", "R", CardRarity.Common, StandardLegal: true, PioneerLegal: true);
}
