namespace MtgaCollectionAdvisor.Core.Creators;

/// <summary>When a channel's feed was last fetched, and how it went.</summary>
public sealed record CreatorFeedState(
    string Creator,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    int ConsecutiveFailures);

/// <summary>
/// When each channel's feed may be fetched again. Per channel, so one broken feed never
/// drags the others along, and with a backoff, so a feed that keeps failing costs a
/// handful of requests a day rather than one per visit to the tab.
///
/// YouTube throttles a machine that asks too often: in development, bursts of requests
/// got every feed refused for hours. A real user must never come near that.
/// </summary>
public static class CreatorFeedSchedule
{
    /// <summary>How long a successful fetch is trusted.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>The first retry after a failure; each further failure doubles it, up to <see cref="MaxAge"/>.</summary>
    public static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(15);

    public static bool IsDue(CreatorFeedState? state, DateTimeOffset now)
    {
        if (state is null) return true;

        if (state.ConsecutiveFailures == 0)
        {
            return state.LastSuccessAt is not { } success || now - success >= MaxAge;
        }

        return state.LastAttemptAt is not { } attempt || now - attempt >= Backoff(state.ConsecutiveFailures);
    }

    /// <summary>15 min, 30 min, 1 h, 2 h, 4 h, then every 6 h.</summary>
    public static TimeSpan Backoff(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;

        var doublings = Math.Min(consecutiveFailures - 1, 10);
        var wait = TimeSpan.FromTicks(FirstRetry.Ticks << doublings);
        return wait < MaxAge ? wait : MaxAge;
    }

    public static CreatorFeedState Record(CreatorFeedState? previous, string creator, bool succeeded, DateTimeOffset now) =>
        succeeded
            ? new CreatorFeedState(creator, LastSuccessAt: now, LastAttemptAt: now, ConsecutiveFailures: 0)
            : new CreatorFeedState(
                creator,
                LastSuccessAt: previous?.LastSuccessAt,
                LastAttemptAt: now,
                ConsecutiveFailures: (previous?.ConsecutiveFailures ?? 0) + 1);
}
