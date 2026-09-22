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

    public string Status { get; private set; } = "Ready.";
    public bool IsBusy { get; private set; }
    public string? BusyOperation { get; private set; }

    public CollectionSnapshot Collection { get; private set; } = CollectionSnapshot.Empty;
    public IReadOnlyList<DeckAnalysisResult> Decks { get; private set; } = [];
    public IReadOnlyDictionary<string, PinnedDeck> Pins { get; private set; } = new Dictionary<string, PinnedDeck>();
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
            StartupError = $"Could not open the local database: {ex.Message}";
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

    public Task ScanCollectionAsync() => RunAsync("Capturing collection", async report =>
    {
        var result = await services.MemoryCollectionSyncService.SyncAutomaticallyAsync(new Progress<string>(report));
        if (result is null)
        {
            report("Could not find the collection in memory. Open MTG Arena and visit the Collection screen.");
            return;
        }
        report($"Collection captured: {result.DistinctCards} cards ({result.TotalCopies} copies).");
        await ReloadRankingAsync();
    });

    public Task FetchDecksAsync() => RunAsync($"Fetching {Format.DisplayName} decks", async report =>
    {
        var format = Format;
        report($"Querying Archidekt ({format.DisplayName})...");
        var decks = await services.ArchidektClient.FetchTopDecksAsync(format, count: 60);

        var unique = DeckDeduplicator.Deduplicate(decks);
        report($"{decks.Count} decks received, {unique.Count} after removing duplicate lists.");

        await services.CuratedDeckStore.ReplaceAutoFetchedAsync(format, ArchidektClient.SourcePrefix, unique);
        await ReloadRankingAsync();
    });

    public Task RefreshCardDatabaseAsync() => RunAsync("Updating card database", async report =>
    {
        report("Downloading Scryfall bulk data (a few minutes)...");
        await services.CardDatabaseStore.ReplaceAllAsync(services.ScryfallBulkImporter.ImportAsync());
        CardsUpdatedAt = await services.CardDatabaseStore.GetLastImportedAsync();
        report("Card database updated.");
        await ReloadRankingAsync();
    });

    public Task DeleteDeckAsync(string sourceId) => RunAsync("Removing deck", async report =>
    {
        await services.CuratedDeckStore.DeleteDeckAsync(sourceId);
        report("Deck removed.");
        await ReloadRankingAsync();
    });

    public string ExportDeck(CandidateDeck deck) => ArenaDeckListWriter.Write(deck);

    public bool IsPinned(string sourceId) => Pins.ContainsKey(sourceId);

    /// <summary>
    /// Pins or unpins without refetching anything - the ranking in memory is unchanged,
    /// only which decks are marked.
    /// </summary>
    public async Task TogglePinAsync(DeckAnalysisResult deck)
    {
        if (services is null) return;

        if (IsPinned(deck.Deck.SourceId))
        {
            await services.PinnedDeckStore.UnpinAsync(deck.Deck.SourceId);
        }
        else
        {
            await services.PinnedDeckStore.PinAsync(deck.Deck.SourceId, deck.Deck.FormatKey, deck.Needed.Total);
        }

        Pins = await services.PinnedDeckStore.LoadAsync();
        Notify();
    }

    /// <summary>Card-name autocomplete for the card filters.</summary>
    public Task<IReadOnlyList<string>> SearchCardNamesAsync(string term) =>
        services is null
            ? Task.FromResult<IReadOnlyList<string>>([])
            : services.CardDatabaseStore.SearchNamesAsync(term);

    /// <summary>
    /// The deck imported most recently into the current format, so the UI can take the
    /// user to where it landed rather than leaving them on a list that did not move.
    /// Read once, via <see cref="ConsumeLastImport"/>.
    /// </summary>
    public string? LastImportedSourceId { get; private set; }

    /// <summary>Returns the last import and forgets it, so it is acted on exactly once.</summary>
    public string? ConsumeLastImport()
    {
        var sourceId = LastImportedSourceId;
        LastImportedSourceId = null;
        return sourceId;
    }

    /// <summary>Adds a deck pasted by hand (Arena export format) to the candidate pool.</summary>
    public Task ImportDeckAsync(string name, FormatDefinition format, string decklist) =>
        RunAsync("Importing deck", async report =>
        {
            var cards = ArenaDeckListParser.Parse(decklist);
            if (cards.Count == 0)
            {
                report("No cards recognised in that text.");
                return;
            }

            var deck = new CandidateDeck(
                SourceId: $"{CandidateDeck.ManualSourcePrefix}{Guid.NewGuid()}",
                Name: name,
                Url: "",
                FormatKey: format.Key,
                Popularity: 0,
                Cards: cards,
                FetchedAt: DateTimeOffset.UtcNow);

            await services.CuratedDeckStore.AddDeckAsync(deck);

            if (format.Key == Format.Key)
            {
                LastImportedSourceId = deck.SourceId;
                report($"Deck \"{name}\" imported ({cards.Count} lines).");
                await ReloadRankingAsync();
            }
            else
            {
                // Saying nothing here would look identical to the import failing: the
                // list on screen is for the other format and does not move.
                report($"Deck \"{name}\" imported into {format.DisplayName} " +
                       $"({cards.Count} lines). Switch the format to see it.");
            }
        });

    /// <summary>
    /// Saves an edit to a deck the user owns, keeping its source id so the pin on it -
    /// and the deck's creation date - survive. Re-importing would mint a new id and lose
    /// both.
    /// </summary>
    public Task UpdateDeckAsync(CandidateDeck existing, string name, FormatDefinition format, string decklist) =>
        RunAsync("Saving deck", async report =>
        {
            var cards = ArenaDeckListParser.Parse(decklist);
            if (cards.Count == 0)
            {
                // Abort before writing: a typo must not empty a deck the user already has.
                report("No cards recognised in that text - the deck was left unchanged.");
                return;
            }

            var updated = existing with
            {
                Name = name,
                FormatKey = format.Key,
                Cards = cards
            };

            await services.CuratedDeckStore.UpdateUserDeckAsync(updated);

            var wasPinned = IsPinned(existing.SourceId);
            LastImportedSourceId = updated.SourceId;
            Format = format;
            await ReloadRankingAsync();

            if (wasPinned)
            {
                // The baseline measured progress towards a list that no longer exists.
                // Rebasing is the honest option, and saying so is the rest of it.
                var need = Decks.FirstOrDefault(d => d.Deck.SourceId == updated.SourceId)?.Needed.Total ?? 0;
                await services.PinnedDeckStore.RebaseAsync(updated.SourceId, need);
                Pins = await services.PinnedDeckStore.LoadAsync();
                report($"Deck \"{name}\" updated ({cards.Count} lines). Pin progress now measures from this list.");
            }
            else
            {
                report($"Deck \"{name}\" updated ({cards.Count} lines).");
            }
        });

    private async Task ReloadRankingAsync()
    {
        Collection = await services.CollectionStore.LoadAsync();
        Pins = await services.PinnedDeckStore.LoadAsync();
        var stored = await services.CuratedDeckStore.LoadAsync(Format);

        if (stored.Count == 0)
        {
            Decks = [];
            Notify();
            return;
        }

        // Every ranked deck, nothing dropped. Deciding which of them a given list shows is
        // DeckFilter's job now (DeckFilterCriteria.IncludeUnplayable): filtering here cost
        // an imported deck its only route into the UI, after import had already reported
        // success, and nothing told the user why.
        Decks = await services.DeckRankingService.RankAsync(Format, stored, Collection);
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
            Status = $"Please wait: {BusyOperation} in progress.";
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
            Status = $"{operation} failed: {ex.Message}";
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
