using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Decks;

/// <summary>
/// Thin client over Moxfield's unofficial public deck-search API
/// (api2.moxfield.com). This is not an officially documented/supported API: it can
/// change shape or start rejecting requests without notice. Every call is defensive
/// (per-deck failures are skipped, never crash the whole fetch) and the caller is
/// expected to surface "0 decks fetched" as a visible error rather than silently
/// showing an empty ranking.
/// </summary>
public sealed class MoxfieldClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api2.moxfield.com/") };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MtgaCollectionAdvisor/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<IReadOnlyList<CandidateDeck>> FetchTopDecksAsync(
        FormatDefinition format, int count, CancellationToken ct = default)
    {
        var searchUrl = $"v2/decks/search?format={format.MoxfieldFormatCode}&pageNumber=1&pageSize={count}" +
                        "&sortType=velocity&sortDirection=Descending&board=mainboard";

        MoxfieldSearchResponse? searchResponse;
        try
        {
            searchResponse = await httpClient.GetFromJsonAsync<MoxfieldSearchResponse>(searchUrl, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new MoxfieldUnavailableException(
                $"Não foi possível buscar decks do Moxfield para {format.DisplayName}.", ex);
        }

        if (searchResponse is null || searchResponse.Data.Count == 0)
        {
            throw new MoxfieldUnavailableException(
                $"O Moxfield não retornou nenhum deck para {format.DisplayName}.");
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var decks = new List<CandidateDeck>(searchResponse.Data.Count);

        foreach (var summary in searchResponse.Data)
        {
            var deck = await TryFetchDeckDetailAsync(summary, format, fetchedAt, ct);
            if (deck is not null) decks.Add(deck);
        }

        if (decks.Count == 0)
        {
            throw new MoxfieldUnavailableException(
                $"O Moxfield listou decks para {format.DisplayName}, mas nenhum pôde ser lido em detalhe.");
        }

        return decks;
    }

    private async Task<CandidateDeck?> TryFetchDeckDetailAsync(
        MoxfieldSearchResult summary, FormatDefinition format, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        try
        {
            var detail = await httpClient.GetFromJsonAsync<MoxfieldDeckDetail>(
                $"v3/decks/all/{summary.PublicId}", JsonOptions, ct);
            if (detail is null) return null;

            var cards = new List<DeckCardRef>();
            foreach (var (name, board) in detail.Mainboard)
                cards.Add(new DeckCardRef(name, board.Quantity, DeckBoard.Main));
            foreach (var (name, board) in detail.Sideboard)
                cards.Add(new DeckCardRef(name, board.Quantity, DeckBoard.Sideboard));

            if (cards.Count == 0) return null;

            return new CandidateDeck(
                SourceId: summary.PublicId,
                Name: string.IsNullOrWhiteSpace(detail.Name) ? summary.Name : detail.Name,
                Url: $"https://www.moxfield.com/decks/{summary.PublicId}",
                FormatKey: format.Key,
                Popularity: summary.Likes,
                Cards: cards,
                FetchedAt: fetchedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null; // skip this one deck, keep going with the rest
        }
    }
}

public sealed class MoxfieldUnavailableException : Exception
{
    public MoxfieldUnavailableException(string message) : base(message) { }
    public MoxfieldUnavailableException(string message, Exception inner) : base(message, inner) { }
}
