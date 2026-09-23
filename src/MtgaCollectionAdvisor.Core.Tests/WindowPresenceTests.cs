using MtgaCollectionAdvisor.Core.Hosting;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// When the app stops for lack of a window. Stopping too eagerly kills it on a reload;
/// never stopping leaves it watching MTG Arena with nobody looking.
/// </summary>
public class WindowPresenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(45);

    [Fact]
    public void ShouldStop_Should_BeFalse_BeforeAnyWindowConnected()
    {
        var presence = new WindowPresence(Grace);

        Assert.False(presence.ShouldStop(T0.AddHours(1)));
    }

    [Fact]
    public void ShouldStop_Should_BeFalse_WhileAWindowIsConnected()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();

        Assert.False(presence.ShouldStop(T0.AddHours(1)));
    }

    [Fact]
    public void ShouldStop_Should_WaitTheGracePeriod_AfterTheLastWindowCloses()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();
        presence.Disconnected(T0);

        Assert.False(presence.ShouldStop(T0.AddSeconds(44)));
        Assert.True(presence.ShouldStop(T0.AddSeconds(45)));
    }

    [Fact]
    public void ShouldStop_Should_BeFalse_When_AWindowReturnsWithinTheGracePeriod()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();
        presence.Disconnected(T0);

        // A reload: the old connection drops, a new one comes up a moment later.
        presence.Connected();

        Assert.False(presence.ShouldStop(T0.AddMinutes(10)));
    }

    [Fact]
    public void ShouldStop_Should_RestartTheCountdown_OnTheNextDisconnect()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();
        presence.Disconnected(T0);
        presence.Connected();
        presence.Disconnected(T0.AddSeconds(30));

        Assert.False(presence.ShouldStop(T0.AddSeconds(60)));
        Assert.True(presence.ShouldStop(T0.AddSeconds(75)));
    }

    [Fact]
    public void ShouldStop_Should_WaitForAllWindows()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();
        presence.Connected();
        presence.Disconnected(T0);

        Assert.False(presence.ShouldStop(T0.AddMinutes(10)));
    }

    [Fact]
    public void Disconnected_Should_NotGoBelowZero()
    {
        var presence = new WindowPresence(Grace);
        presence.Connected();
        presence.Disconnected(T0);
        presence.Disconnected(T0);

        // Had the count gone to -1, this window would leave it at 0 and read as "no window".
        presence.Connected();

        Assert.False(presence.ShouldStop(T0.AddMinutes(10)));
    }

    [Fact]
    public void DefaultGracePeriod_Should_Be45Seconds()
    {
        var presence = new WindowPresence();
        presence.Connected();
        presence.Disconnected(T0);

        Assert.False(presence.ShouldStop(T0.AddSeconds(44)));
        Assert.True(presence.ShouldStop(T0.AddSeconds(45)));
    }
}
