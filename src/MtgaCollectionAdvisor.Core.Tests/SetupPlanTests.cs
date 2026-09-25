using MtgaCollectionAdvisor.Core.Decks;
using MtgaCollectionAdvisor.Core.Onboarding;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public sealed class SetupPlanTests
{
    private static readonly SetupFacts Nothing = new(
        InProgress: false, HasCards: false, HasStandardDecks: false, CreatorVideosEnabled: true, HasCreatorVideos: false, HasCollection: false);

    private static readonly SetupFacts Everything = new(
        InProgress: false, HasCards: true, HasStandardDecks: true, CreatorVideosEnabled: true, HasCreatorVideos: true, HasCollection: true);

    [Fact]
    public void IsNeeded_Should_BeTrue_When_CardsMissing()
    {
        Assert.True(SetupPlan.IsNeeded(Everything with { HasCards = false }));
    }

    [Fact]
    public void IsNeeded_Should_BeTrue_When_StandardDecksMissing()
    {
        Assert.True(SetupPlan.IsNeeded(Everything with { HasStandardDecks = false }));
    }

    [Fact]
    public void IsNeeded_Should_BeFalse_When_OnlyCollectionOrCreatorsMissing()
    {
        Assert.False(SetupPlan.IsNeeded(Everything with { HasCollection = false, HasCreatorVideos = false }));
    }

    // Closed midway after the cards and some decks were stored: the rest of the setup (creators,
    // MTG Arena) must still happen, though the app is already usable.
    [Fact]
    public void IsNeeded_Should_BeTrue_When_ASetupWasLeftUnfinished()
    {
        Assert.True(SetupPlan.IsNeeded(Everything with { InProgress = true, HasCollection = false }));
        Assert.Equal([SetupStep.Collection], SetupPlan.StepsFor(Everything with { InProgress = true, HasCollection = false }));
    }

    [Fact]
    public void StepsFor_Should_ListMissingStepsInOrder()
    {
        Assert.Equal(
            [SetupStep.CardDatabase, SetupStep.Decks, SetupStep.CreatorVideos, SetupStep.Collection],
            SetupPlan.StepsFor(Nothing));
    }

    [Fact]
    public void StepsFor_Should_LeaveOutCreators_When_FeatureIsOff()
    {
        Assert.DoesNotContain(SetupStep.CreatorVideos, SetupPlan.StepsFor(Nothing with { CreatorVideosEnabled = false }));
    }

    [Fact]
    public void StepsFor_Should_KeepOnlyMissingSteps_When_Resuming()
    {
        Assert.Equal(
            [SetupStep.Decks, SetupStep.CreatorVideos, SetupStep.Collection],
            SetupPlan.StepsFor(Nothing with { HasCards = true }));
    }

    private static readonly SetupStep[] AllSteps =
        [SetupStep.CardDatabase, SetupStep.Decks, SetupStep.CreatorVideos, SetupStep.Collection];

    [Fact]
    public void Next_Should_ReturnFirstPendingStep()
    {
        var states = new Dictionary<SetupStep, SetupStepState> { [SetupStep.CardDatabase] = SetupStepState.Done };

        Assert.Equal(SetupStep.Decks, SetupPlan.Next(states, AllSteps));
    }

    [Fact]
    public void Next_Should_StayOnFailedStep()
    {
        var states = new Dictionary<SetupStep, SetupStepState>
        {
            [SetupStep.CardDatabase] = SetupStepState.Done,
            [SetupStep.Decks] = SetupStepState.Failed,
        };

        Assert.Equal(SetupStep.Decks, SetupPlan.Next(states, AllSteps));
    }

    [Fact]
    public void Next_Should_MovePastSkippedSteps()
    {
        var states = new Dictionary<SetupStep, SetupStepState>
        {
            [SetupStep.CardDatabase] = SetupStepState.Done,
            [SetupStep.Decks] = SetupStepState.Skipped,
        };

        Assert.Equal(SetupStep.CreatorVideos, SetupPlan.Next(states, AllSteps));
    }

    [Fact]
    public void Next_Should_ReturnNull_When_AllDoneOrSkipped()
    {
        var states = AllSteps.ToDictionary(s => s, s => s == SetupStep.Collection ? SetupStepState.Skipped : SetupStepState.Done);

        Assert.Null(SetupPlan.Next(states, AllSteps));
    }

    [Theory]
    [InlineData(SetupStep.CardDatabase)]
    [InlineData(SetupStep.Decks)]
    [InlineData(SetupStep.CreatorVideos)]
    [InlineData(SetupStep.Collection)]
    public void IsSatisfied_Should_ReflectEachFact(SetupStep step)
    {
        Assert.True(SetupPlan.IsSatisfied(step, Everything));
        Assert.False(SetupPlan.IsSatisfied(step, Nothing));
    }

    [Fact]
    public void IsSatisfied_Should_TreatCreatorsAsDone_When_FeatureIsOff()
    {
        Assert.True(SetupPlan.IsSatisfied(SetupStep.CreatorVideos, Nothing with { CreatorVideosEnabled = false }));
    }

    // A first fetch has no "last fetch": the usual "no new decks since the last fetch" would
    // read as success on the setup screen.
    [Fact]
    public void DescribeEmptyPool_Should_SayArchidektWasUnreachable_When_Stopped()
    {
        var report = new DeckSyncReport(0, 0, 0, 0, 0, CutShort: false,
            StoppedBecause: "Could not search Standard decks on Archidekt.", CooldownRemaining: null);

        Assert.Equal("Could not reach Archidekt: Could not search Standard decks on Archidekt.", report.DescribeEmptyPool());
    }

    [Fact]
    public void DescribeEmptyPool_Should_AskToWait_When_InCooldown()
    {
        var report = new DeckSyncReport(0, 0, 0, 0, 0, CutShort: false,
            StoppedBecause: null, CooldownRemaining: TimeSpan.FromMinutes(3.2));

        Assert.Contains("try again in 4 min", report.DescribeEmptyPool());
    }

    [Fact]
    public void DescribeEmptyPool_Should_SayNothingWasFound_Otherwise()
    {
        var report = new DeckSyncReport(0, 0, 0, 0, 0, CutShort: false, StoppedBecause: null, CooldownRemaining: null);

        Assert.Equal("Archidekt returned no recent Standard decks.", report.DescribeEmptyPool());
    }
}
