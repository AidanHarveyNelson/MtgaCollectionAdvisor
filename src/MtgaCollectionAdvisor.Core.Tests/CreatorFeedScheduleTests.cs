using MtgaCollectionAdvisor.Core.Creators;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// How often a real user's app may ask YouTube for a channel's feed. The numbers matter:
/// in development, asking too often got every feed refused for hours.
/// </summary>
public class CreatorFeedScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsDue_Should_BeTrue_When_ChannelWasNeverFetched()
    {
        Assert.True(CreatorFeedSchedule.IsDue(null, Now));
    }

    [Fact]
    public void IsDue_Should_WaitMaxAge_After_ASuccess()
    {
        Assert.False(CreatorFeedSchedule.IsDue(Succeeded(hoursAgo: 5.9), Now));
        Assert.True(CreatorFeedSchedule.IsDue(Succeeded(hoursAgo: 6), Now));
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(5, 240)]
    [InlineData(6, 360)]
    [InlineData(40, 360)]
    public void Backoff_Should_DoubleFrom15Minutes_UpToMaxAge(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), CreatorFeedSchedule.Backoff(failures));
    }

    [Fact]
    public void IsDue_Should_FollowTheBackoff_After_Failures()
    {
        var twiceFailed = new CreatorFeedState("Bob", LastSuccessAt: null, LastAttemptAt: Now.AddMinutes(-20), ConsecutiveFailures: 2);

        Assert.False(CreatorFeedSchedule.IsDue(twiceFailed, Now));               // 20 min < 30 min
        Assert.True(CreatorFeedSchedule.IsDue(twiceFailed, Now.AddMinutes(10)));  // 30 min
    }

    [Fact]
    public void Record_Should_CountFailures_And_KeepTheLastSuccess()
    {
        var ok = CreatorFeedSchedule.Record(null, "Bob", succeeded: true, Now.AddHours(-7));
        var failed = CreatorFeedSchedule.Record(ok, "Bob", succeeded: false, Now);
        var failedAgain = CreatorFeedSchedule.Record(failed, "Bob", succeeded: false, Now.AddMinutes(15));

        Assert.Equal(2, failedAgain.ConsecutiveFailures);
        Assert.Equal(Now.AddHours(-7), failedAgain.LastSuccessAt);
        Assert.Equal(Now.AddMinutes(15), failedAgain.LastAttemptAt);
    }

    [Fact]
    public void Record_Should_ResetFailures_On_Success()
    {
        var failing = new CreatorFeedState("Bob", null, Now.AddHours(-1), ConsecutiveFailures: 4);

        var recovered = CreatorFeedSchedule.Record(failing, "Bob", succeeded: true, Now);

        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.False(CreatorFeedSchedule.IsDue(recovered, Now.AddHours(1)));
    }

    [Fact]
    public void Schedule_Should_KeepABrokenFeed_ToAFewAttemptsADay()
    {
        // A channel whose feed fails all day, visited constantly: at most the backoff's
        // attempts, never one per visit.
        CreatorFeedState? state = null;
        var attempts = 0;
        for (var at = Now; at < Now.AddDays(1); at = at.AddMinutes(1))
        {
            if (!CreatorFeedSchedule.IsDue(state, at)) continue;
            attempts++;
            state = CreatorFeedSchedule.Record(state, "Bob", succeeded: false, at);
        }

        Assert.InRange(attempts, 1, 9);
    }

    private static CreatorFeedState Succeeded(double hoursAgo) =>
        new("Alice", LastSuccessAt: Now.AddHours(-hoursAgo), LastAttemptAt: Now.AddHours(-hoursAgo), ConsecutiveFailures: 0);
}
