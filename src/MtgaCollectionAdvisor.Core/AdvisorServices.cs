using MtgaCollectionAdvisor.Core.Analysis;
using MtgaCollectionAdvisor.Core.Cards;
using MtgaCollectionAdvisor.Core.Configuration;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Logs;
using MtgaCollectionAdvisor.Core.Storage;
using Npgsql;

namespace MtgaCollectionAdvisor.Core;

/// <summary>
/// Manual composition root - this app is small enough that a full DI container would
/// be pure ceremony, so callers (the WinForms app, tests) just new this up once.
/// </summary>
public sealed class AdvisorServices : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; }
    public CollectionStore CollectionStore { get; }
    public CardDatabaseStore CardDatabaseStore { get; }
    public DeckCacheStore DeckCacheStore { get; }
    public CollectionBrowserQuery CollectionBrowserQuery { get; }
    public DeckRankingService DeckRankingService { get; }
    public PlayerLogWatcher PlayerLogWatcher { get; }
    public HttpClient ScryfallHttpClient { get; }
    public HttpClient MoxfieldHttpClient { get; }
    public ScryfallBulkImporter ScryfallBulkImporter { get; }
    public MoxfieldClient MoxfieldClient { get; }

    private AdvisorServices(AppConfig config)
    {
        DataSource = NpgsqlDataSource.Create(config.PostgresConnectionString);
        CollectionStore = new CollectionStore(DataSource);
        CardDatabaseStore = new CardDatabaseStore(DataSource);
        DeckCacheStore = new DeckCacheStore(DataSource);
        CollectionBrowserQuery = new CollectionBrowserQuery(DataSource);
        DeckRankingService = new DeckRankingService(new WildcardCalculator(CardDatabaseStore));
        PlayerLogWatcher = new PlayerLogWatcher(config.PlayerLogPathOverride);

        ScryfallHttpClient = ScryfallBulkImporter.CreateHttpClient();
        ScryfallBulkImporter = new ScryfallBulkImporter(ScryfallHttpClient);

        MoxfieldHttpClient = Decks.MoxfieldClient.CreateHttpClient();
        MoxfieldClient = new MoxfieldClient(MoxfieldHttpClient);
    }

    public static async Task<AdvisorServices> CreateAsync(AppConfig config, CancellationToken ct = default)
    {
        var services = new AdvisorServices(config);
        await SchemaInitializer.EnsureCreatedAsync(services.DataSource, ct);
        return services;
    }

    public async ValueTask DisposeAsync()
    {
        PlayerLogWatcher.Dispose();
        ScryfallHttpClient.Dispose();
        MoxfieldHttpClient.Dispose();
        await DataSource.DisposeAsync();
    }
}
