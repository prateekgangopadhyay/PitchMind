using System.Text.Json;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class FixtureCorrectionHistoryTests
{
    [Fact]
    public void ResolvingCorrectionPreservesSourceIdentityAndTimeAndLeavesPriorFixtureUnchanged()
    {
        var fixture = SyntheticFixtureGenerator.Generate(seed: 123);
        var goal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var correction = ReplaceHomeGoalWithShot(goal, fixture.Provenance, revision: 2, id: "correction-v2");
        var sourceEventsBefore = fixture.Events.ToArray();

        var first = FixtureCorrectionHistory.Resolve(fixture, [correction]);
        var repeated = FixtureCorrectionHistory.Resolve(fixture, [correction]);
        var corrected = first.Single(item => item.Event.Event.EventId == goal.Event.EventId);
        var originalState = Replay(fixture, fixture.Events).Snapshot();
        var correctedState = Replay(fixture, first.Select(item => item.Event)).Snapshot();
        var repeatedState = Replay(fixture, repeated.Select(item => item.Event)).Snapshot();

        Assert.Equal(goal.Event.EventId, corrected.Event.Event.EventId);
        Assert.Equal(goal.Event.MatchId, corrected.Event.Event.MatchId);
        Assert.Equal(goal.Event.Sequence, corrected.Event.Event.Sequence);
        Assert.Equal(goal.Event.Period, corrected.Event.Event.Period);
        Assert.Equal(goal.Event.ClockSeconds, corrected.Event.Event.ClockSeconds);
        Assert.Equal(goal.Event.OccurredAt, corrected.Event.Event.OccurredAt);
        Assert.Equal(fixture.Provenance, corrected.SourceProvenance);
        Assert.Equal(correction.Provenance, corrected.CorrectionProvenance);
        Assert.Equal(correction.CorrectionId, corrected.CorrectionId);
        Assert.Equal(correction.Revision, corrected.CorrectionRevision);
        Assert.Equal(sourceEventsBefore, fixture.Events);
        Assert.Equal(originalState.HomeScore - 1, correctedState.HomeScore);
        Assert.Equal(originalState.AwayScore, correctedState.AwayScore);
        Assert.Equal(originalState.LastSequence, correctedState.LastSequence);
        Assert.Equal(originalState.ClockSeconds, correctedState.ClockSeconds);
        Assert.Equal(JsonSerializer.Serialize(correctedState), JsonSerializer.Serialize(repeatedState));
        Assert.Equal(fixture.Events.Select(item => item.Event.Sequence), first.Select(item => item.Event.Event.Sequence));
    }

    [Fact]
    public void LatestCorrectionRevisionWinsAndSameRevisionConflictIsRejected()
    {
        var fixture = SyntheticFixtureGenerator.Generate(seed: 123);
        var goal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var correction = ReplaceHomeGoalWithShot(goal, fixture.Provenance, revision: 2, id: "correction-v2");
        var restore = new FixtureEventCorrection(3, "correction-v3", goal.Event.EventId, fixture.Provenance, goal);

        var resolved = FixtureCorrectionHistory.Resolve(fixture, [restore, correction]);
        var selected = resolved.Single(item => item.Event.Event.EventId == goal.Event.EventId);

        Assert.Equal(MatchEventType.Goal, selected.Event.Event.Type);
        Assert.Equal(3, selected.CorrectionRevision);
        Assert.Equal("correction-v3", selected.CorrectionId);
        Assert.Equal(1, Replay(fixture, resolved.Select(item => item.Event)).Snapshot().HomeScore);

        var conflicting = new FixtureEventCorrection(2, "correction-v2-other", goal.Event.EventId, fixture.Provenance, goal);
        var failure = Assert.Throws<InvalidOperationException>(() =>
            FixtureCorrectionHistory.Resolve(fixture, [correction, conflicting]));
        Assert.Contains("conflicting replacements", failure.Message);
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("matchId")]
    [InlineData("sequence")]
    [InlineData("period")]
    [InlineData("clock")]
    [InlineData("occurredAt")]
    public void CorrectionRejectsChangesToSourceIdentityOrMatchTime(string changedField)
    {
        var fixture = SyntheticFixtureGenerator.Generate(seed: 123);
        var source = fixture.Events.First(item => item.Event.Type == MatchEventType.Pass);
        var replacementEvent = changedField switch
        {
            "eventId" => source.Event with { EventId = Guid.NewGuid() },
            "matchId" => source.Event with { MatchId = new MatchId(Guid.NewGuid()) },
            "sequence" => source.Event with { Sequence = source.Event.Sequence + 1 },
            "period" => source.Event with { Period = source.Event.Period == 1 ? 2 : 1 },
            "clock" => source.Event with { ClockSeconds = source.Event.ClockSeconds + 1 },
            "occurredAt" => source.Event with { OccurredAt = source.Event.OccurredAt.AddSeconds(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var correction = new FixtureEventCorrection(2, "invalid-correction", source.Event.EventId, fixture.Provenance,
            source with { Event = replacementEvent });

        var failure = Assert.Throws<InvalidOperationException>(() => FixtureCorrectionHistory.Resolve(fixture, [correction]));
        Assert.Contains("preserve the original event ID", failure.Message);
    }

    [Fact]
    public void CorrectionRejectsUnknownTargetsAndContradictoryGoalEnvelope()
    {
        var fixture = SyntheticFixtureGenerator.Generate(seed: 123);
        var goal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var unknownTarget = new FixtureEventCorrection(2, "unknown-target", Guid.NewGuid(), fixture.Provenance, goal);
        var unknownFailure = Assert.Throws<InvalidOperationException>(() => FixtureCorrectionHistory.Resolve(fixture, [unknownTarget]));
        Assert.Contains("target does not exist", unknownFailure.Message);

        var contradictory = new FixtureEventCorrection(2, "contradictory-goal", goal.Event.EventId, fixture.Provenance,
            goal with { Event = goal.Event with { Type = MatchEventType.Shot } });
        var goalFailure = Assert.Throws<InvalidOperationException>(() => FixtureCorrectionHistory.Resolve(fixture, [contradictory]));
        Assert.Contains("Goal event type, action, and scored outcome", goalFailure.Message);
    }

    [Fact]
    public void CorrectionRejectsMissingReplacementEventPayload()
    {
        var fixture = SyntheticFixtureGenerator.Generate(seed: 123);
        var source = fixture.Events.First();
        var correction = new FixtureEventCorrection(2, "missing-event-payload", source.Event.EventId, fixture.Provenance,
            source with { Event = null! });

        var failure = Assert.Throws<InvalidOperationException>(() => FixtureCorrectionHistory.Resolve(fixture, [correction]));
        Assert.Contains("Corrected event payload is required", failure.Message);
    }

    private static FixtureEventCorrection ReplaceHomeGoalWithShot(
        SimulationEvent goal, string provenance, long revision, string id) =>
        new(revision, id, goal.Event.EventId, provenance,
            goal with { Event = goal.Event with { Type = MatchEventType.Shot }, Action = "shot", Outcome = "on-target" });

    private static MatchAggregate Replay(SyntheticFixture fixture, IEnumerable<SimulationEvent> events)
    {
        var players = fixture.Home.Players.Concat(fixture.Away.Players).Select(player => (player.Id, player.TeamId));
        var activePlayers = fixture.Frames.Single(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId);
        var rules = new MatchCompetitionRules(fixture.CompetitionId, fixture.CompetitionRulesVersion,
            fixture.MaximumSubstituteReentriesPerPlayer);
        var aggregate = new MatchAggregate(new MatchId(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id,
            players, activePlayers, fixture.Provenance, rules);
        foreach (var item in events) aggregate.Apply(item);
        return aggregate;
    }
}
