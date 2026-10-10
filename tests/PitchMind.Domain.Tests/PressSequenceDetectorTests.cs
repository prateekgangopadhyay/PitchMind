using PitchMind.Domain.Analytics;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class PressSequenceDetectorTests
{
    private const int ScenarioGoalSeconds = 3309;

    [Fact]
    public void DetectsVersionedSyntheticSequenceWithOrderedEvidence()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var input = CreateInput(fixture, ScenarioGoalSeconds);

        var insight = Assert.Single(PressSequenceDetector.Detect(input));

        Assert.Equal("press-sequence-v2", insight.DetectorVersion);
        Assert.Equal("unapproved-demo", insight.PolicyStatus);
        Assert.False(insight.Publishable);
        Assert.Equal(ScenarioGoalSeconds, insight.ClockSeconds);
        Assert.Equal(input.SelectedTimeSeconds, insight.SelectedTimeSeconds);
        Assert.Equal(input.ThroughSequence, insight.ThroughSequence);
        Assert.Equal(input.SourceVersion, insight.SourceVersion);
        var actionsByEventId = input.Events.ToDictionary(item => item.Event.EventId, item => item.Action);
        Assert.Equal(new[] { "back-pass", "press-arrival", "pressured-pass", "high-regain", "cutback", "shot", "goal" },
            insight.EventIds.Select(eventId => actionsByEventId[eventId]));
        Assert.Equal(insight.EventIds.Count, insight.FrameIds.Count);
        Assert.All(insight.EventIds, eventId => Assert.Contains(eventId, input.Events.Select(item => item.Event.EventId)));
        Assert.All(insight.FrameIds, frameId => Assert.Contains(frameId, input.Frames.Select(frame => frame.Id)));
    }

    [Fact]
    public void IdenticalVersionedInputProducesIdenticalDetection()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var input = CreateInput(fixture, ScenarioGoalSeconds);

        var first = Assert.Single(PressSequenceDetector.Detect(input));
        var second = Assert.Single(PressSequenceDetector.Detect(input));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.MatchId, second.MatchId);
        Assert.Equal(first.ClockSeconds, second.ClockSeconds);
        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.DetectorVersion, second.DetectorVersion);
        Assert.Equal(first.EventIds, second.EventIds);
        Assert.Equal(first.FrameIds, second.FrameIds);
        Assert.Equal(first.Conditions, second.Conditions);
    }

    [Fact]
    public void NoTrackingCannotSupportSpatialPressureClaim()
    {
        var fixture = SyntheticFixtureGenerator.Generate();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, frames: [])));
    }

    [Fact]
    public void MissingPressureEventFrameCannotSupportSpatialPressureClaim()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Where(frame => frame.ClockSeconds != 3293).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, frames: frames)));
    }

    [Fact]
    public void CompletedOpponentPassIsNotMislabeledAsPressInducedFailure()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(item => item.Action == "pressured-pass"
            ? item with { Outcome = "complete" }
            : item).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, events: events)));
    }

    [Fact]
    public void SameTeamPressureDoesNotQualifyAsOppositionPressure()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var trigger = fixture.Events.Single(item => item.Action == "back-pass");
        var events = fixture.Events.Select(item => item.Action == "press-arrival"
            ? item with { TeamId = trigger.TeamId }
            : item).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, events: events)));
    }

    [Fact]
    public void UnrelatedTeamTurnoverIsNotAttributedToPressureSequence()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var pressureTeam = fixture.Events.Single(item => item.Action == "press-arrival").TeamId;
        var unrelatedTeam = fixture.Events.Single(item => item.Action == "back-pass").TeamId;
        var events = fixture.Events.Select(item => item.Action == "high-regain"
            ? item with { TeamId = unrelatedTeam }
            : item).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, events: events)));
    }

    [Fact]
    public void NormalCirculationEventsAreNotIncludedInDetectedSequence()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var ordinaryCirculation = fixture.Events
            .Where(item => item.Action == "circulation" && item.Event.Type == PitchMind.Domain.Events.MatchEventType.Pass)
            .ToArray();

        Assert.NotEmpty(ordinaryCirculation);
        Assert.All(ordinaryCirculation, item => Assert.Equal("circulation", item.Action));
        var insight = Assert.Single(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds)));
        Assert.DoesNotContain(insight.EventIds, id => ordinaryCirculation.Any(item => item.Event.EventId == id));
    }

    [Fact]
    public void GoalAfterSelectedTimeIsExcludedFromReplayPrefix()
    {
        var fixture = SyntheticFixtureGenerator.Generate();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds - 1)));
    }

    [Fact]
    public void PressureOutsideVersionedDistanceIsRejected()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select(frame => frame.ClockSeconds == 3293
            ? frame with
            {
                Players = frame.Players.Select(player => player.PlayerId == "harbour-10"
                    ? player with { X = 40 }
                    : player).ToArray()
            }
            : frame).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, ScenarioGoalSeconds, frames: frames)));
    }

    [Fact]
    public void RejectsSkippedSequenceInClaimedCompletePrefix()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var completePrefix = fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        var skippedPrefix = completePrefix.Where((_, index) => index != 1).ToArray();
        var input = new PressSequenceInput(
            fixture.MatchId,
            fixture.Version,
            fixture.Provenance,
            null,
            5400,
            completePrefix[^1].Event.Sequence,
            fixture.Home.Id,
            fixture.Home.Name,
            fixture.Away.Id,
            fixture.Away.Name,
            "stadium-metres",
            fixture.PitchLengthMetres,
            fixture.PitchWidthMetres,
            skippedPrefix,
            fixture.Frames);

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(input));
    }

    [Fact]
    public void RejectsNonSyntheticProvenance()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        var input = new PressSequenceInput(
            fixture.MatchId,
            fixture.Version,
            "live-provider",
            null,
            5400,
            events[^1].Event.Sequence,
            fixture.Home.Id,
            fixture.Home.Name,
            fixture.Away.Id,
            fixture.Away.Name,
            "stadium-metres",
            fixture.PitchLengthMetres,
            fixture.PitchWidthMetres,
            events,
            fixture.Frames);

        Assert.Throws<ArgumentException>(() => PressSequenceDetector.Detect(input));
    }

    private static PressSequenceInput CreateInput(SyntheticFixture fixture, int selectedTimeSeconds,
        IReadOnlyList<SimulationEvent>? events = null, IReadOnlyList<TrackingFrame>? frames = null)
    {
        var visibleEvents = (events ?? fixture.Events)
            .Where(item => item.Event.ClockSeconds <= selectedTimeSeconds)
            .OrderBy(item => item.Event.ClockSeconds)
            .ThenBy(item => item.Event.Sequence)
            .ToArray();

        return new PressSequenceInput(
            fixture.MatchId,
            fixture.Version,
            fixture.Provenance,
            null,
            selectedTimeSeconds,
            visibleEvents.Length == 0 ? -1 : visibleEvents[^1].Event.Sequence,
            fixture.Home.Id,
            fixture.Home.Name,
            fixture.Away.Id,
            fixture.Away.Name,
            "stadium-metres",
            fixture.PitchLengthMetres,
            fixture.PitchWidthMetres,
            Array.AsReadOnly(visibleEvents),
            Array.AsReadOnly((frames ?? fixture.Frames)
                .Where(frame => frame.ClockSeconds <= selectedTimeSeconds)
                .ToArray()));
    }
}
