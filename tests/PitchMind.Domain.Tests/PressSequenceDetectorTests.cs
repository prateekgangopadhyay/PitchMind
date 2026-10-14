using PitchMind.Domain.Analytics;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
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
    public void CirculationOnlyPrefixDoesNotProducePressInsight()
    {
        var fixture = SyntheticFixtureGenerator.Generate();

        Assert.Empty(PressSequenceDetector.Detect(CreateInput(fixture, 3289)));
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
            fixture.Frames,
            new string('a', 64));

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(input));
    }

    [Fact]
    public void SuppressesPressureDistanceWhenSameTimeTrackingIsAmbiguous()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var pressure = fixture.Events.Single(item => item.Action == "press-arrival");
        var pressureFrame = fixture.Frames.Single(frame => frame.ClockSeconds == pressure.Event.ClockSeconds);
        var frames = fixture.Frames.Append(pressureFrame with { Id = pressureFrame.Id + "-duplicate" }).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(Input(fixture, frames)));
    }

    [Fact]
    public void RejectsDuplicateSourceEventIds()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.ToArray();
        events[^1] = events[^1] with
        {
            Event = events[^1].Event with { EventId = events[0].Event.EventId }
        };

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(Input(fixture, events: events)));
    }

    [Fact]
    public void RejectsDuplicatePlayerIdsInTrackingFrameInsteadOfThrowingDuringDetection()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var pressure = fixture.Events.Single(item => item.Action == "press-arrival");
        var frames = fixture.Frames.Select(frame => frame.ClockSeconds == pressure.Event.ClockSeconds
            ? frame with { Players = frame.Players.Append(frame.Players[0]).ToArray() }
            : frame).ToArray();

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(Input(fixture, frames)));
    }

    private static PressSequenceInput Input(SyntheticFixture fixture,
        IReadOnlyList<TrackingFrame>? frames = null, IReadOnlyList<SimulationEvent>? events = null)
    {
        var selectedEvents = events ?? fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        return new PressSequenceInput(fixture.MatchId, fixture.Version, fixture.Provenance, null, 5400,
            selectedEvents.Count == 0 ? -1 : selectedEvents[^1].Event.Sequence,
            fixture.Home.Id, fixture.Home.Name, fixture.Away.Id, fixture.Away.Name, "stadium-metres",
            fixture.PitchLengthMetres, fixture.PitchWidthMetres, selectedEvents, frames ?? fixture.Frames,
            new string('a', 64));
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
            fixture.Frames,
            new string('a', 64));

        Assert.Throws<ArgumentException>(() => PressSequenceDetector.Detect(input));
    }

    [Fact]
    public void RejectsPublishingGraphWithoutApprovedRevisionedCanonicalEvidence()
    {
        var sourceHash = new string('a', 64);
        var claim = new TacticalEvidenceNode("claim:one", "claim", "pattern", "claim", null, null,
            10, 1, "synthetic-v2", sourceHash, null, null, null);
        var source = new TacticalEvidenceNode("event:one", "event", "fact", "source", Guid.NewGuid(), null,
            9, 1, "synthetic-v2", sourceHash, null, null, null);
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", sourceHash, "synthetic", null, 10, 1,
            "press-sequence-v2", "unapproved-demo", true, claim.Id,
            new[] { claim, source }, new[] { new TacticalEvidenceEdge(claim.Id, source.Id, "derived_from") },
            "uncalibrated", new[] { new TacticalEvidenceCondition("source", true, "observed", null, new[] { source.Id }, null) });

        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));
    }

    [Fact]
    public void SelectedContextMetricLinksDetectorEventsAndActiveRosterEvidence()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var initialPlayers = fixture.Home.Players.Concat(fixture.Away.Players)
            .Select(player => (player.Id, player.TeamId));
        var activeAtKickoff = fixture.Frames.Single(frame => frame.ClockSeconds == 0)
            .Players.Select(player => player.PlayerId);
        var aggregate = new MatchAggregate(new MatchId(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id,
            initialPlayers, activeAtKickoff, fixture.Provenance,
            new MatchCompetitionRules(fixture.CompetitionId, fixture.CompetitionRulesVersion,
                fixture.MaximumSubstituteReentriesPerPlayer));
        foreach (var item in fixture.Events.Where(item => item.Event.ClockSeconds <= 5400)) aggregate.Apply(item);
        var selectedState = aggregate.Snapshot() with { ClockSeconds = 5400 };
        var insight = Assert.Single(PressSequenceDetector.Detect(fixture, 5400, selectedState));
        var graph = Assert.IsType<TacticalEvidenceGraph>(insight.EvidenceGraph);
        var contextMetricId = $"metric:{insight.Id}:selected-context";
        var contextSourceIds = graph.Edges.Where(edge => edge.FromNodeId == contextMetricId && edge.Relation == "derived_from")
            .Select(edge => edge.ToNodeId).ToHashSet(StringComparer.Ordinal);
        var roster = Assert.Single(graph.Nodes, node => node.Id == $"metric:{insight.Id}:active-roster");
        var expectedRosterIds = selectedState.Players.Where(player => player.Active)
            .Select(player => $"{player.TeamId}/{player.PlayerId}");
        Assert.Contains(expectedRosterIds.First(), roster.Description, StringComparison.Ordinal);
        Assert.Contains(roster.Id, contextSourceIds);

        var contextEventIds = fixture.Events.Where(item => item.Event.ClockSeconds <= 5400 &&
            (insight.EventIds.Contains(item.Event.EventId) || item.Event.Type is MatchEventType.Goal or
                MatchEventType.Substitution or MatchEventType.RedCard or MatchEventType.YellowCard or
                MatchEventType.KickOff or MatchEventType.HalfTime or MatchEventType.FullTime))
            .Select(item => $"event:{item.Event.EventId:D}").Distinct(StringComparer.Ordinal);
        Assert.All(contextEventIds, eventId => Assert.Contains(eventId, contextSourceIds));
        var contextCondition = Assert.Single(graph.Conditions, condition => condition.Id == "selected-context");
        Assert.Contains(roster.Id, contextCondition.SourceNodeIds);
        Assert.All(contextEventIds, eventId => Assert.Contains(eventId, contextCondition.SourceNodeIds));
    }

    [Fact]
    public void EventNodeSequenceMustNotExceedGraphThroughSequence()
    {
        var sourceHash = new string('a', 64);
        var claim = new TacticalEvidenceNode("claim:one", "claim", "pattern", "claim", null, null,
            10, 1, "synthetic-v2", sourceHash, null, null, null);
        var lateEvent = new TacticalEvidenceNode("event:late", "event", "fact", "late source event", Guid.NewGuid(), null,
            9, 2, "synthetic-v2", sourceHash, null, null, null);
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", sourceHash, "synthetic", null,
            10, 1, "press-sequence-v2", "unapproved-demo", false, claim.Id,
            new[] { claim, lateEvent }, new[] { new TacticalEvidenceEdge(claim.Id, lateEvent.Id, "derived_from") },
            "Uncalibrated.", new[] { new TacticalEvidenceCondition("source", true, "observed", null, new[] { lateEvent.Id }, null) });

        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));
    }

    [Fact]
    public void GraphMustContainExactlyOneClaimNode()
    {
        var sourceHash = new string('a', 64);
        var claim = new TacticalEvidenceNode("claim:one", "claim", "pattern", "claim", null, null,
            10, 1, "synthetic-v2", sourceHash, null, null, null);
        var extraClaim = claim with { Id = "claim:two" };
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", sourceHash, "synthetic", null,
            10, 1, "press-sequence-v2", "unapproved-demo", false, claim.Id,
            new[] { claim, extraClaim }, Array.Empty<TacticalEvidenceEdge>(), "Uncalibrated.",
            new[] { new TacticalEvidenceCondition("test", true, "observed", null, Array.Empty<string>(), null) });

        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));
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
                .ToArray()),
            new string('a', 64));
    }
}
