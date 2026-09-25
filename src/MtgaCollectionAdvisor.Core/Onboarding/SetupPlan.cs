namespace MtgaCollectionAdvisor.Core.Onboarding;

/// <summary>The steps of the first-run setup, in the order they run.</summary>
public enum SetupStep { CardDatabase, Decks, CreatorVideos, Collection }

public enum SetupStepState { Pending, Running, Done, Failed, Skipped }

/// <summary>
/// What the stores hold right now, read at startup and after every step. <c>InProgress</c> is
/// a setup that started and never finished, closed midway: it resumes even though the cards
/// and decks it already stored would no longer call for it.
/// </summary>
public sealed record SetupFacts(
    bool InProgress,
    bool HasCards,
    bool HasStandardDecks,
    bool CreatorVideosEnabled,
    bool HasCreatorVideos,
    bool HasCollection);

/// <summary>
/// Decides whether a new player sees the setup screen and what it runs next. Driven by the
/// data that is missing, so a player who deletes their data is set up again, plus whether a
/// setup was left unfinished, so a run closed midway resumes where it stopped.
/// </summary>
public static class SetupPlan
{
    private static readonly SetupStep[] Order =
        [SetupStep.CardDatabase, SetupStep.Decks, SetupStep.CreatorVideos, SetupStep.Collection];

    /// <summary>
    /// When the app is unusable (no card database or no Standard decks), or a setup was left
    /// unfinished. A missing collection alone does not count, or a player without MTG Arena
    /// open would get the setup screen on every start.
    /// </summary>
    public static bool IsNeeded(SetupFacts facts) => facts.InProgress || !facts.HasCards || !facts.HasStandardDecks;

    /// <summary>The steps a run shows, in order: every step whose data is missing.</summary>
    public static IReadOnlyList<SetupStep> StepsFor(SetupFacts facts) =>
        Order.Where(step => step != SetupStep.CreatorVideos || facts.CreatorVideosEnabled)
             .Where(step => !IsSatisfied(step, facts))
             .ToList();

    public static bool IsSatisfied(SetupStep step, SetupFacts facts) => step switch
    {
        SetupStep.CardDatabase => facts.HasCards,
        SetupStep.Decks => facts.HasStandardDecks,
        SetupStep.CreatorVideos => !facts.CreatorVideosEnabled || facts.HasCreatorVideos,
        SetupStep.Collection => facts.HasCollection,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    /// <summary>
    /// The step to work on: the first one not Done or Skipped, or null when setup is over. A
    /// Failed step stays current, so the player decides between Retry and Skip; nothing
    /// moves past it silently.
    /// </summary>
    public static SetupStep? Next(IReadOnlyDictionary<SetupStep, SetupStepState> states, IReadOnlyList<SetupStep> order)
    {
        foreach (var step in order)
        {
            var state = states.GetValueOrDefault(step, SetupStepState.Pending);
            if (state is not (SetupStepState.Done or SetupStepState.Skipped)) return step;
        }

        return null;
    }
}
