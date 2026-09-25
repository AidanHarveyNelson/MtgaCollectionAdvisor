using System.Text.Json;
using MtgaCollectionAdvisor.Core.Cards;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// Which image a card gets, from Scryfall's own JSON shapes (trimmed to the fields that
/// matter). Parsed the way ScryfallBulkImporter parses bulk data.
/// </summary>
public sealed class ScryfallImageTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static ScryfallCard Parse(string json) => JsonSerializer.Deserialize<ScryfallCard>(json, Options)!;

    [Fact]
    public void NormalImageUrls_Should_UseTopLevelImage_ForSingleFacedCard()
    {
        var card = Parse("""
            {"name":"Locke Cole","image_uris":{"small":"https://cards.scryfall.io/small/front/5/7/a.jpg","normal":"https://cards.scryfall.io/normal/front/5/7/a.jpg"}}
            """);

        Assert.Equal(("https://cards.scryfall.io/normal/front/5/7/a.jpg", (string?)null), card.NormalImageUrls());
    }

    [Fact]
    public void NormalImageUrls_Should_UseEachFace_ForDoubleFacedCard()
    {
        var card = Parse("""
            {"name":"Ojer Taq, Deepest Foundation // Temple of Civilization","layout":"transform","card_faces":[
              {"name":"Ojer Taq, Deepest Foundation","image_uris":{"normal":"https://cards.scryfall.io/normal/front/1/2/b.jpg"}},
              {"name":"Temple of Civilization","image_uris":{"normal":"https://cards.scryfall.io/normal/back/1/2/b.jpg"}}]}
            """);

        Assert.Equal(("https://cards.scryfall.io/normal/front/1/2/b.jpg", "https://cards.scryfall.io/normal/back/1/2/b.jpg"),
            card.NormalImageUrls());
    }

    [Fact]
    public void NormalImageUrls_Should_UseTopLevelImage_ForSplitOrAdventureCard()
    {
        var card = Parse("""
            {"name":"Bonecrusher Giant // Stomp","layout":"adventure","image_uris":{"normal":"https://cards.scryfall.io/normal/front/3/4/c.jpg"},
             "card_faces":[{"name":"Bonecrusher Giant","mana_cost":"{2}{R}"},{"name":"Stomp","mana_cost":"{1}{R}"}]}
            """);

        Assert.Equal(("https://cards.scryfall.io/normal/front/3/4/c.jpg", (string?)null), card.NormalImageUrls());
    }

    [Fact]
    public void NormalImageUrls_Should_BeNull_When_NoImages()
    {
        Assert.Equal(((string?)null, (string?)null), Parse("""{"name":"Nothing"}""").NormalImageUrls());
        Assert.Equal(((string?)null, (string?)null),
            Parse("""{"name":"Faces","card_faces":[{"name":"A"},{"name":"B"}]}""").NormalImageUrls());
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://cards.scryfall.io/normal/front/a.jpg")]
    [InlineData("javascript:alert(1)")]
    public void NormalImageUrls_Should_IgnoreNonHttpsValues(string url)
    {
        var card = Parse($$$"""{"name":"Odd","image_uris":{"normal":"{{{url}}}"}}""");

        Assert.Equal(((string?)null, (string?)null), card.NormalImageUrls());
    }
}
