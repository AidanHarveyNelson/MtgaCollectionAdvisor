using MtgaCollectionAdvisor.Core.Logs;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public class LogMessageParserTests
{
    // Real line shapes captured from a live Player.log during development.
    private static readonly string[] InventoryInfoLines =
    [
        "[UnityCrossThreadLogger]20/09/2026 18:13:09",
        "<== StartHook(dc7be6cf-ea4b-4265-ba4f-103083f88b4b)",
        """{ "InventoryInfo": { "SeqId": 1, "Gems": 2630, "Gold": 18525, "TotalVaultProgress": 947, "WcTrackPosition": 21, "WildCardCommons": 119, "WildCardUnCommons": 62, "WildCardRares": 4, "WildCardMythics": 11, "CustomTokens": { "PlayInToken": 5 } } }""",
        """[UnityCrossThreadLogger]==> GraphGetGraphState {"id":"9e39aba1-f381-454e-a133-1cc1deff2b61","request":"{\"GraphId\":\"Achievements_Colors\"}"}"""
    ];

    [Fact]
    public void ParsesInventoryInfoFromResponsePayload()
    {
        var parser = new LogMessageParser();
        WildcardInventory? captured = null;

        foreach (var line in InventoryInfoLines)
        {
            var evt = parser.ProcessLine(line);
            if (evt is not null && LogEventInterpreter.TryGetInventoryInfo(evt, out var inv))
            {
                captured = inv;
            }
        }

        Assert.NotNull(captured);
        Assert.Equal(119, captured!.Commons);
        Assert.Equal(62, captured.Uncommons);
        Assert.Equal(4, captured.Rares);
        Assert.Equal(11, captured.Mythics);
    }

    [Fact]
    public void IgnoresOutgoingRequestsAndTimestampOnlyLines()
    {
        var parser = new LogMessageParser();
        Assert.Null(parser.ProcessLine("[UnityCrossThreadLogger]20/09/2026 18:13:10"));
        Assert.Null(parser.ProcessLine(
            """[UnityCrossThreadLogger]==> GraphGetGraphState {"id":"abc","request":"{\"GraphId\":\"X\"}"}"""));
    }

    [Fact]
    public void DetectsPlayerCardsCollectionDump()
    {
        var parser = new LogMessageParser();

        var grpIdToQty = Enumerable.Range(70000, 200).ToDictionary(id => id, id => id % 5);
        var json = "{" + string.Join(",", grpIdToQty.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";

        LogEvent? capturedEvent = null;
        capturedEvent ??= parser.ProcessLine("[UnityCrossThreadLogger]20/09/2026 18:13:11");
        capturedEvent = parser.ProcessLine("<== PlayerInventory.GetPlayerCardsV3(11111111-1111-1111-1111-111111111111)");
        Assert.Null(capturedEvent);

        capturedEvent = parser.ProcessLine(json);
        Assert.NotNull(capturedEvent);

        var isCollection = LogEventInterpreter.TryGetPlayerCards(capturedEvent!, out var owned);
        Assert.True(isCollection);
        Assert.Equal(200, owned.Count);
        Assert.Equal(3, owned[70003]);
    }

    [Fact]
    public void SmallNumericObjectIsNotMistakenForCollectionDump()
    {
        var parser = new LogMessageParser();
        parser.ProcessLine("<== SomeSmallCall(11111111-1111-1111-1111-111111111111)");
        var evt = parser.ProcessLine("""{"1":1,"2":2}""");

        Assert.NotNull(evt);
        Assert.False(LogEventInterpreter.TryGetPlayerCards(evt!, out _));
    }
}
