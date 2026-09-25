using MtgaCollectionAdvisor.Core.Logs;
using MtgaCollectionAdvisor.Core.Models;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>#57: unknown wildcards are explained, from what Player.log says about Detailed Logs.</summary>
public sealed class WildcardStatusTests
{
    [Theory]
    [InlineData("DETAILED LOGS: ENABLED", true)]
    [InlineData("DETAILED LOGS: DISABLED", false)]
    [InlineData("  DETAILED LOGS: ENABLED  ", true)]
    public void DetailedLogsLine_Should_ReadEnabledAndDisabled(string line, bool expected)
    {
        Assert.Equal(expected, DetailedLogsLine.Parse(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[UnityCrossThreadLogger]Client.SceneChange")]
    [InlineData("{\"InventoryInfo\":{\"WildCardCommons\":3}}")]
    [InlineData("Some text then DETAILED LOGS: ENABLED")]
    [InlineData("DETAILED LOGS: MAYBE")]
    public void DetailedLogsLine_Should_IgnoreOtherLines(string line)
    {
        Assert.Null(DetailedLogsLine.Parse(line));
    }

    [Theory]
    [InlineData(DetailedLogs.Disabled, true)]
    [InlineData(DetailedLogs.Enabled, true)]
    [InlineData(DetailedLogs.Unknown, false)]
    public void Hint_Should_BeNull_When_WildcardsKnown(DetailedLogs detailedLogs, bool logExists)
    {
        Assert.Null(WildcardStatus.Hint(wildcardsKnown: true, detailedLogs, logExists));
    }

    [Fact]
    public void Hint_Should_AskToEnableDetailedLogs_When_Disabled()
    {
        var hint = WildcardStatus.Hint(wildcardsKnown: false, DetailedLogs.Disabled, logExists: true);

        Assert.Contains("Turn on Detailed Logs", hint);
        Assert.Contains("Options → Account", hint);
    }

    [Fact]
    public void Hint_Should_AskToLogIn_When_EnabledButNotRead()
    {
        Assert.StartsWith("Log in to MTG Arena", WildcardStatus.Hint(wildcardsKnown: false, DetailedLogs.Enabled, logExists: true));
    }

    [Theory]
    [InlineData(DetailedLogs.Unknown, true)]
    [InlineData(DetailedLogs.Unknown, false)]
    [InlineData(DetailedLogs.Enabled, false)]
    public void Hint_Should_AskToOpenArena_When_NoLogOrUnknown(DetailedLogs detailedLogs, bool logExists)
    {
        Assert.StartsWith("Open MTG Arena", WildcardStatus.Hint(wildcardsKnown: false, detailedLogs, logExists));
    }
}
