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

    private static async IAsyncEnumerable<CardInfo> Cards()
    {
        yield return Card(86952, "Mosswood Dreadknight // Dread Whispers");
        yield return Card(1, "Lightning Bolt");
        yield return Card(2, "Lightning Bolt");
        yield return Card(3, "Shock");
        await Task.CompletedTask;
    }

    private static CardInfo Card(int grpId, string name) =>
        new(grpId, name, "TST", "{R}", "R", CardRarity.Common, StandardLegal: true, PioneerLegal: true);
}
