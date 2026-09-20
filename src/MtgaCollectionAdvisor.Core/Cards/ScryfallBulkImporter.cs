using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Cards;

/// <summary>
/// Downloads Scryfall's public "default_cards" bulk data file and projects it down to
/// the subset of fields this app needs, keyed by Arena's grpId (Scryfall's arena_id).
/// Per Scryfall's guidance, bulk data (not the live per-card API) is the correct way
/// to obtain the full card database.
/// </summary>
public sealed class ScryfallBulkImporter(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MtgaCollectionAdvisor", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromMinutes(10);
        return client;
    }

    public async IAsyncEnumerable<CardInfo> ImportAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var listResponse = await httpClient.GetFromJsonAsync<ScryfallBulkDataResponse>(
            "https://api.scryfall.com/bulk-data", JsonOptions, ct);

        var defaultCards = listResponse?.Data.FirstOrDefault(d => d.Type == "default_cards")
            ?? throw new InvalidOperationException("Scryfall bulk-data listing did not contain a 'default_cards' entry.");

        await using var stream = await httpClient.GetStreamAsync(defaultCards.DownloadUri, ct);

        await foreach (var card in JsonSerializer.DeserializeAsyncEnumerable<ScryfallCard>(stream, JsonOptions, ct))
        {
            if (card is null || card.ArenaId is null) continue;

            yield return new CardInfo(
                GrpId: card.ArenaId.Value,
                Name: card.Name,
                SetCode: card.Set,
                ManaCost: card.EffectiveManaCost,
                Colors: card.EffectiveColors(),
                Rarity: MapRarity(card),
                StandardLegal: card.IsLegal("standard"),
                PioneerLegal: card.IsLegal("pioneer"));
        }
    }

    private static CardRarity MapRarity(ScryfallCard card)
    {
        if (card.TypeLine.Contains("Basic Land", StringComparison.OrdinalIgnoreCase)) return CardRarity.Basic;
        return card.Rarity switch
        {
            "common" => CardRarity.Common,
            "uncommon" => CardRarity.Uncommon,
            "rare" => CardRarity.Rare,
            "mythic" => CardRarity.Mythic,
            _ => CardRarity.Unknown
        };
    }
}
