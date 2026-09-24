using MtgaCollectionAdvisor.Core.Creators;
using MtgaCollectionAdvisor.Core.Storage;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public sealed class CreatorVideoStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"advisor-test-{Guid.NewGuid():N}.db");
    private CreatorVideoStore _store = null!;

    public async Task InitializeAsync()
    {
        var database = new Database(_databasePath);
        await SchemaMigrator.MigrateAsync(database);
        _store = new CreatorVideoStore(database);
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Load_Should_ReturnEmpty_OnFreshDatabase()
    {
        var snapshot = await _store.LoadAsync();

        Assert.Empty(snapshot.Feeds);
        Assert.Empty(snapshot.Videos);
    }

    [Fact]
    public async Task Replace_Then_Load_Should_RoundTripVideosAndFeedStates()
    {
        var at = new DateTimeOffset(2026, 9, 23, 10, 30, 0, TimeSpan.Zero);
        var feeds = new Dictionary<string, CreatorFeedState>
        {
            ["Crokeyz"] = new("Crokeyz", LastSuccessAt: at, LastAttemptAt: at, ConsecutiveFailures: 0),
            ["Bob"] = new("Bob", LastSuccessAt: null, LastAttemptAt: at, ConsecutiveFailures: 3)
        };
        var videos = new[]
        {
            Video("inline", DeckSourceKind.InlineList, decklist: "Deck\n4 Shock"),
            Video("arch", DeckSourceKind.Archidekt, archidektId: 12345),
            Video("ext", DeckSourceKind.External, site: "AetherHub", url: "https://aetherhub.com/Deck/Public/1"),
            Video("none", DeckSourceKind.None) with { Language = "pt" }
        };

        await _store.ReplaceAsync(new CreatorVideoSnapshot(videos, feeds));
        var loaded = await _store.LoadAsync();

        Assert.Equal(feeds["Crokeyz"], loaded.FeedOf("Crokeyz"));
        Assert.Equal(feeds["Bob"], loaded.FeedOf("Bob"));
        Assert.Equal(videos.OrderBy(v => v.VideoId), loaded.Videos.OrderBy(v => v.VideoId));
    }

    [Fact]
    public async Task Replace_Should_RemoveRowsNotInTheNewSnapshot()
    {
        var none = new Dictionary<string, CreatorFeedState>();
        await _store.ReplaceAsync(new CreatorVideoSnapshot([Video("gone", DeckSourceKind.None), Video("kept", DeckSourceKind.None)], none));

        await _store.ReplaceAsync(new CreatorVideoSnapshot([Video("kept", DeckSourceKind.None)], none));

        Assert.Equal(["kept"], (await _store.LoadAsync()).Videos.Select(v => v.VideoId));
    }

    private static CreatorVideo Video(
        string id, DeckSourceKind kind,
        string? decklist = null, int? archidektId = null, string? site = null, string? url = null) =>
        new(id, "Crokeyz", $"Title {id}", new DateTimeOffset(2026, 9, 22, 15, 0, 12, TimeSpan.Zero),
            kind, decklist, archidektId, site, url);
}
