using MtgaCollectionAdvisor.Core;
using MtgaCollectionAdvisor.Core.Configuration;
using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Memory;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Web.Services;

/// <summary>
/// Holds everything the UI shows and owns the long-running operations (memory scan,
/// deck fetch, card database import). Components subscribe to <see cref="Changed"/>
/// and re-render; nothing in the UI ever blocks on a scan.
/// </summary>
public sealed class AdvisorSession(AppConfig config) : IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private System.Threading.Timer? _mtgaWatchTimer;
    private bool _mtgaWasRunning;
    private AdvisorServices services = null!;

    public event Action? Changed;

    /// <summary>Set when the app could not start (database unreachable); the UI shows it.</summary>
    public string? StartupError { get; private set; }

    public string Status { get; private set; } = "Pronto.";
    public bool IsBusy { get; private set; }
    public string? BusyOperation { get; private set; }

    public CollectionSnapshot Collection { get; private set; } = CollectionSnapshot.Empty;
    public IReadOnlyList<DeckAnalysisResult> Decks { get; private set; } = [];
    public FormatDefinition Format { get; private set; } = Formats.Standard;
    public DateTimeOffset? CardsUpdatedAt { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            services = await AdvisorServices.CreateAsync(config);
        }
        catch (Exception ex)
        {
            StartupError = $"Não consegui abrir o banco local: {ex.Message}";
            Status = StartupError;
            Notify();
            return;
        }

        CardsUpdatedAt = await services.CardDatabaseStore.GetLastImportedAsync();
        services.PlayerLogWatcher.InventoryUpdated += OnWildcardsUpdated;
        services.PlayerLogWatcher.Start();

        await ReloadRankingAsync();

        _mtgaWatchTimer = new System.Threading.Timer(_ => _ = AutoScanIfGameStartedAsync(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(20));
    }

    public async Task SetFormatAsync(FormatDefinition format)
    {
        Format = format;
        await ReloadRankingAsync();
    }

    public Task ScanCollectionAsync() => RunAsync("Capturando coleção", async report =>
    {
        var result = await services.MemoryCollectionSyncService.SyncAutomaticallyAsync(new Progress<string>(report));
        if (result is null)
        {
            report("Não encontrei a coleção na memória. Abra o MTG Arena e visite a tela de Coleção.");
            return;
        }
        report($"Coleção capturada: {result.DistinctCards} cartas ({result.TotalCopies} cópias).");
        await ReloadRankingAsync();
    });

    public Task FetchDecksAsync() => RunAsync($"Buscando decks de {Format.DisplayName}", async report =>
    {
        var format = Format;
        report($"Consultando a Archidekt ({format.DisplayName})...");
        var decks = await services.ArchidektClient.FetchTopDecksAsync(format, count: 60);

        var unique = DeckDeduplicator.Deduplicate(decks);
        report($"{decks.Count} decks recebidos, {unique.Count} após remover listas repetidas.");

        await services.CuratedDeckStore.ReplaceAutoFetchedAsync(format, ArchidektClient.SourcePrefix, unique);
        await ReloadRankingAsync();
    });

    public Task RefreshCardDatabaseAsync() => RunAsync("Atualizando base de cartas", async report =>
    {
        report("Baixando bulk data da Scryfall (alguns minutos)...");
        await services.CardDatabaseStore.ReplaceAllAsync(services.ScryfallBulkImporter.ImportAsync());
        CardsUpdatedAt = await services.CardDatabaseStore.GetLastImportedAsync();
        report("Base de cartas atualizada.");
        await ReloadRankingAsync();
    });

    public Task DeleteDeckAsync(string sourceId) => RunAsync("Removendo deck", async report =>
    {
        await services.CuratedDeckStore.DeleteDeckAsync(sourceId);
        report("Deck removido.");
        await ReloadRankingAsync();
    });

    public string ExportDeck(CandidateDeck deck) => ArenaDeckListWriter.Write(deck);

    /// <summary>Adds a deck pasted by hand (Arena export format) to the candidate pool.</summary>
    public Task ImportDeckAsync(string name, FormatDefinition format, string decklist) =>
        RunAsync("Importando deck", async report =>
        {
            var cards = ArenaDeckListParser.Parse(decklist);
            if (cards.Count == 0)
            {
                report("Não reconheci nenhuma carta nesse texto.");
                return;
            }

            var deck = new CandidateDeck(
                SourceId: $"manual:{Guid.NewGuid()}",
                Name: name,
                Url: "",
                FormatKey: format.Key,
                Popularity: 0,
                Cards: cards,
                FetchedAt: DateTimeOffset.UtcNow);

            await services.CuratedDeckStore.AddDeckAsync(deck);
            report($"Deck \"{name}\" importado ({cards.Count} linhas).");

            if (format.Key == Format.Key) await ReloadRankingAsync();
        });

    private async Task ReloadRankingAsync()
    {
        Collection = await services.CollectionStore.LoadAsync();
        var stored = await services.CuratedDeckStore.LoadAsync(Format);

        if (stored.Count == 0)
        {
            Decks = [];
            Notify();
            return;
        }

        var ranked = await services.DeckRankingService.RankAsync(Format, stored, Collection);
        Decks = ranked.Where(r => r.LegalInFormat && r.FullyPlayableOnArena).ToList();
        Notify();
    }

    private async Task AutoScanIfGameStartedAsync()
    {
        var running = MemoryCollectionSyncService.IsMtgaRunning();
        var justStarted = running && !_mtgaWasRunning;
        _mtgaWasRunning = running;

        if (justStarted && !IsBusy) await ScanCollectionAsync();
    }

    private void OnWildcardsUpdated(WildcardInventory inventory)
    {
        _ = Task.Run(async () =>
        {
            await services.CollectionStore.SaveWildcardsAsync(inventory);
            Collection = Collection with { Wildcards = inventory };
            Notify();
        });
    }

    private async Task RunAsync(string operation, Func<Action<string>, Task> action)
    {
        if (!await _operationGate.WaitAsync(0))
        {
            Status = $"Aguarde: {BusyOperation} em andamento.";
            Notify();
            return;
        }

        IsBusy = true;
        BusyOperation = operation;
        Status = $"{operation}...";
        Notify();

        try
        {
            await action(message =>
            {
                Status = message;
                Notify();
            });
        }
        catch (Exception ex)
        {
            Status = $"{operation} falhou: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyOperation = null;
            _operationGate.Release();
            Notify();
        }
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        _mtgaWatchTimer?.Dispose();
        _operationGate.Dispose();
    }
}
