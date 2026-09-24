using System.Text.Json;
using MtgaCollectionAdvisor.Core.Arena;
using MtgaCollectionAdvisor.Core.Models;

namespace MtgaCollectionAdvisor.Core.Logs;

/// <summary>
/// Turns a raw <see cref="LogEvent"/> into typed collection/inventory data, when
/// the event is one we care about. Detection is structural (look at the shape of
/// the JSON) rather than purely name-based, because MTGA's RPC method names for
/// the same logical payload have changed across client versions.
/// </summary>
public static class LogEventInterpreter
{
    public static bool TryGetInventoryInfo(LogEvent evt, out WildcardInventory inventory)
    {
        inventory = WildcardInventory.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(evt.Json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("InventoryInfo", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                return TryReadInventoryObject(nested, out inventory);
            }

            if (root.TryGetProperty("WildCardCommons", out _))
            {
                return TryReadInventoryObject(root, out inventory);
            }

            return false;
        }
    }

    private static bool TryReadInventoryObject(JsonElement obj, out WildcardInventory inventory)
    {
        inventory = new WildcardInventory(
            Commons: GetInt(obj, "WildCardCommons"),
            Uncommons: GetInt(obj, "WildCardUnCommons"),
            Rares: GetInt(obj, "WildCardRares"),
            Mythics: GetInt(obj, "WildCardMythics"));
        return true;
    }

    /// <summary>
    /// The full collection dump ("PlayerInventory.GetPlayerCardsV3" and similar) is a
    /// flat JSON object mapping grpId (as a string key) to owned quantity. We detect
    /// it structurally: a large object whose keys are all numeric and whose values are
    /// all non-negative integers.
    /// </summary>
    public static bool TryGetPlayerCards(LogEvent evt, out IReadOnlyDictionary<int, int> ownedByGrpId)
    {
        ownedByGrpId = new Dictionary<int, int>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(evt.Json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            const int minimumPlausibleCollectionSize = 50;
            var properties = root.EnumerateObject().ToList();
            if (properties.Count < minimumPlausibleCollectionSize) return false;

            var result = new Dictionary<int, int>(properties.Count);
            foreach (var prop in properties)
            {
                if (!int.TryParse(prop.Name, out var grpId)) return false;
                if (prop.Value.ValueKind != JsonValueKind.Number) return false;
                if (!prop.Value.TryGetInt32(out var qty) || qty < 0) return false;
                result[grpId] = qty;
            }

            ownedByGrpId = result;
            return true;
        }
    }

    /// <summary>
    /// The decks saved in Arena, from the login message ("StartHook"): names and formats in
    /// <c>DeckSummaries</c>, cards in <c>DecksInternal</c> keyed by deck id. Detected by
    /// shape - both keys present - like the rest of this class.
    ///
    /// Wizards' own decks sit in the same list: Arena's suggested decks carry
    /// <c>IsNetDeck</c>, and precons are named by a localisation key ("?=?Loc/Decks/...").
    /// </summary>
    public static bool TryGetArenaDecks(LogEvent evt, out IReadOnlyList<ArenaDeck> decks)
    {
        decks = [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(evt.Json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("DeckSummaries", out var summaries) || summaries.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("DecksInternal", out var contents) || contents.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var result = new List<ArenaDeck>();
            foreach (var summary in summaries.EnumerateArray())
            {
                var id = GetString(summary, "DeckIdInternal");
                if (id.Length == 0 || !contents.TryGetProperty(id, out var content)) continue;

                var rawName = GetString(summary, "Name");
                var isLocalisationKey = rawName.StartsWith(LocalisationKeyPrefix, StringComparison.Ordinal);
                var isNetDeck = summary.TryGetProperty("IsNetDeck", out var net) && net.ValueKind == JsonValueKind.True;

                result.Add(new ArenaDeck(
                    Id: id,
                    Name: isLocalisationKey ? rawName[(rawName.LastIndexOf('/') + 1)..] : rawName,
                    Format: FormatOf(summary),
                    IsWizardsDeck: isNetDeck || isLocalisationKey,
                    Commander: ReadCards(content, "CommandZone"),
                    Companion: ReadCards(content, "Companions"),
                    Main: ReadCards(content, "MainDeck"),
                    Sideboard: ReadCards(content, "Sideboard")));
            }

            decks = result;
            return true;
        }
    }

    private const string LocalisationKeyPrefix = "?=?Loc/";

    private static string FormatOf(JsonElement summary)
    {
        if (!summary.TryGetProperty("Attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Array) return "";
        foreach (var attribute in attributes.EnumerateArray())
        {
            if (GetString(attribute, "name") == "Format") return GetString(attribute, "value");
        }
        return "";
    }

    /// <summary>A section's cards, with an id listed twice summed into one entry.</summary>
    private static IReadOnlyList<ArenaCard> ReadCards(JsonElement deck, string section)
    {
        if (!deck.TryGetProperty(section, out var cards) || cards.ValueKind != JsonValueKind.Array) return [];

        var byId = new Dictionary<int, int>();
        foreach (var card in cards.EnumerateArray())
        {
            var grpId = GetInt(card, "cardId");
            var quantity = GetInt(card, "quantity");
            if (grpId <= 0 || quantity <= 0) continue;
            byId[grpId] = byId.TryGetValue(grpId, out var existing) ? existing + quantity : quantity;
        }
        return byId.Select(kv => new ArenaCard(kv.Key, kv.Value)).ToList();
    }

    private static string GetString(JsonElement obj, string propertyName)
        => obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int GetInt(JsonElement obj, string propertyName)
        => obj.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var i) ? i : 0;
}
