using PitchMind.Domain.Simulation;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace PitchMind.Domain.Tests;
public sealed class SyntheticFixtureTests
{
    [Fact] public void FixtureIsRepeatableAndComplete()
    {
        var first = SyntheticFixtureGenerator.Generate(); var second = SyntheticFixtureGenerator.Generate();
        Assert.Equal(first.Events, second.Events); Assert.Equal(5400, first.DurationSeconds);
        Assert.Equal(MatchEventType.KickOff, first.Events[0].Event.Type);
        Assert.Equal(MatchEventType.FullTime, first.Events[^1].Event.Type);
        Assert.Equal(Enumerable.Range(0, first.Events.Count).Select(i => (long)i), first.Events.Select(e => e.Event.Sequence));
        Assert.Equal(first.Events.Count, first.Events.Select(e => e.Event.EventId).Distinct().Count());
        Assert.All(first.Frames, frame => { Assert.Equal(22, frame.Players.Count); Assert.All(frame.Players, p => { Assert.InRange(p.X, 0, 105); Assert.InRange(p.Y, 0, 68); }); });
    }
    [Fact] public void AttackDirectionChangesAtHalfTime()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        Assert.Equal(5, fixture.Frames.Single(f => f.ClockSeconds == 2670).Players.Single(p => p.PlayerId == "harbour-1").X);
        Assert.Equal(100, fixture.Frames.Single(f => f.ClockSeconds == 2700).Players.Single(p => p.PlayerId == "harbour-1").X);
    }

    [Fact] public void SeedAndFixtureVersionProduceStableIdentitiesAndFrames()
    {
        var first = SyntheticFixtureGenerator.Generate(123);
        var second = SyntheticFixtureGenerator.Generate(123);
        var third = SyntheticFixtureGenerator.Generate(123);
        var differentSeed = SyntheticFixtureGenerator.Generate(124);

        Assert.Equal(first.MatchId, second.MatchId);
        Assert.NotEqual(first.MatchId, differentSeed.MatchId);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(JsonSerializer.Serialize(first.Frames), JsonSerializer.Serialize(second.Frames));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        Assert.Equal(Fingerprint(second), Fingerprint(third));
        Assert.NotEqual(first.Events[0].Event.EventId, differentSeed.Events[0].Event.EventId);
        Assert.Equal("synthetic-v2", first.Version);
        Assert.Equal("synthetic", first.Provenance);
    }

    [Fact] public void VersionedSyntheticFixtureMatchesGoldenFingerprint()
    {
        var fixture = SyntheticFixtureGenerator.Generate(123);
        Assert.Equal("4d0e5cba88bfaaf596946bf7ebb50dcca18262e0bed3506ededeaf440189d67b", Fingerprint(fixture));

        var players = fixture.Home.Players.Concat(fixture.Away.Players).Select(player => (player.Id, player.TeamId));
        var activePlayers = fixture.Frames.Single(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId);
        var aggregate = new MatchAggregate(new(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id,
            players, activePlayers, fixture.Provenance);
        foreach (var item in fixture.Events) aggregate.Apply(item);
        var stateJson = JsonSerializer.Serialize(aggregate.Snapshot());
        var stateFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        Assert.Equal("e18412b8a6c72dba5a73f5dcebc01cacee644db0938750486cdb97a05736176b", stateFingerprint);
    }

    [Fact] public void CorrectedFixtureReplayPreservesIdentityTimeAndProvenanceAndChangesStateDeterministically()
    {
        var fixture = SyntheticFixtureGenerator.Generate(123);
        var originalGoal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var correction = new FixtureEventCorrection(
            2,
            "correction-home-goal-classification-v1",
            originalGoal.Event.EventId,
            fixture.Provenance,
            originalGoal with { Event = originalGoal.Event with { Type = MatchEventType.Shot }, Action = "shot", Outcome = "on-target" });

        var correctedEvents = FixtureCorrectionHistory.Resolve(fixture, [correction]);
        var repeatedEvents = FixtureCorrectionHistory.Resolve(fixture, [correction]);
        var originalState = Replay(fixture, fixture.Events).Snapshot();
        var correctedState = Replay(fixture, correctedEvents.Select(item => item.Event)).Snapshot();
        var repeatedState = Replay(fixture, repeatedEvents.Select(item => item.Event)).Snapshot();

        var unchangedEvents = FixtureCorrectionHistory.Resolve(fixture, []);
        Assert.Equal(fixture.Events, unchangedEvents.Select(item => item.Event));
        Assert.All(unchangedEvents, item => Assert.Equal(fixture.Provenance, item.SourceProvenance));
        Assert.All(unchangedEvents, item => Assert.Null(item.CorrectionProvenance));
        Assert.Equal(fixture.Provenance, correctedState.Provenance);
        Assert.Equal((0, 1), (correctedState.HomeScore, correctedState.AwayScore));
        Assert.Equal(originalState.LastSequence, correctedState.LastSequence);
        Assert.Equal(originalState.ClockSeconds, correctedState.ClockSeconds);
        Assert.Equal(JsonSerializer.Serialize(correctedState), JsonSerializer.Serialize(repeatedState));
        Assert.Equal("92e3808bd73bb4e6596128a78078fbeabf01b1f33ec807d1bafa32fc3c31d6ba", Fingerprint(correctedState));
        Assert.Equal(originalGoal, fixture.Events.Single(item => item.Event.EventId == originalGoal.Event.EventId));

        var correctedGoalSlot = correctedEvents.Single(item => item.Event.Event.EventId == originalGoal.Event.EventId);
        Assert.Equal(originalGoal.Event.EventId, correctedGoalSlot.Event.Event.EventId);
        Assert.Equal(originalGoal.Event.MatchId, correctedGoalSlot.Event.Event.MatchId);
        Assert.Equal(originalGoal.Event.Sequence, correctedGoalSlot.Event.Event.Sequence);
        Assert.Equal(originalGoal.Event.Period, correctedGoalSlot.Event.Event.Period);
        Assert.Equal(originalGoal.Event.ClockSeconds, correctedGoalSlot.Event.Event.ClockSeconds);
        Assert.Equal(originalGoal.Event.OccurredAt, correctedGoalSlot.Event.Event.OccurredAt);
        Assert.Equal("shot", correctedGoalSlot.Event.Action);
        Assert.Equal("on-target", correctedGoalSlot.Event.Outcome);
        Assert.Equal(fixture.Provenance, correctedGoalSlot.SourceProvenance);
        Assert.Equal(correction.Provenance, correctedGoalSlot.CorrectionProvenance);
        Assert.Equal(correction.CorrectionId, correctedGoalSlot.CorrectionId);
        Assert.Equal(correction.Revision, correctedGoalSlot.CorrectionRevision);
        Assert.Equal(fixture.Events.Select(item => item.Event.Sequence), correctedEvents.Select(item => item.Event.Event.Sequence));
    }

    [Fact] public void LatestCorrectionRevisionWinsWithoutMutatingEarlierHistory()
    {
        var fixture = SyntheticFixtureGenerator.Generate(123);
        var goal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var first = new FixtureEventCorrection(2, "correction-v2", goal.Event.EventId, fixture.Provenance,
            goal with { Event = goal.Event with { Type = MatchEventType.Shot } });
        var second = new FixtureEventCorrection(3, "correction-v3", goal.Event.EventId, fixture.Provenance, goal);

        var afterFirst = FixtureCorrectionHistory.Resolve(fixture, [first]);
        var afterBoth = FixtureCorrectionHistory.Resolve(fixture, [second, first]);

        Assert.Equal(MatchEventType.Shot, afterFirst.Single(item => item.Event.Event.EventId == goal.Event.EventId).Event.Event.Type);
        Assert.Equal(MatchEventType.Goal, afterBoth.Single(item => item.Event.Event.EventId == goal.Event.EventId).Event.Event.Type);
        Assert.Equal(1, Replay(fixture, afterBoth.Select(item => item.Event)).Snapshot().HomeScore);
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("matchId")]
    [InlineData("sequence")]
    [InlineData("period")]
    [InlineData("clock")]
    [InlineData("occurredAt")]
    public void CorrectionRejectsChangesToEventIdentityOrMatchTime(string changedField)
    {
        var fixture = SyntheticFixtureGenerator.Generate(123);
        var source = fixture.Events.First(item => item.Event.Type == MatchEventType.Pass);
        var replacementEvent = changedField switch
        {
            "eventId" => source.Event with { EventId = Guid.NewGuid() },
            "matchId" => source.Event with { MatchId = new(Guid.NewGuid()) },
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

    [Fact] public void CorrectionHistoryRejectsConflictingCorrectionsAtTheSameRevision()
    {
        var fixture = SyntheticFixtureGenerator.Generate(123);
        var goal = fixture.Events.Single(item => item.Event.Type == MatchEventType.Goal && item.TeamId == fixture.Home.Id);
        var first = new FixtureEventCorrection(2, "correction-a", goal.Event.EventId, fixture.Provenance,
            goal with { Event = goal.Event with { Type = MatchEventType.Shot }, Action = "shot", Outcome = "on-target" });
        var conflict = new FixtureEventCorrection(2, "correction-b", goal.Event.EventId, fixture.Provenance, goal);

        var failure = Assert.Throws<InvalidOperationException>(() => FixtureCorrectionHistory.Resolve(fixture, [first, conflict]));
        Assert.Contains("conflicting replacements", failure.Message);
    }

    [Fact] public void ScenarioIncludesPeriodsCardsSubstitutionAndLegalScoreTransitions()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events;
        Assert.Contains(events, e => e.Event.Type == MatchEventType.HalfTime && e.Event.Period == 1);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.KickOff && e.Event.Period == 2);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.YellowCard);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.Substitution && e.RelatedPlayerId is not null);
        Assert.Equal(5400, events[^1].Event.ClockSeconds);

        var home = 0;
        var away = 0;
        foreach (var item in events)
        {
            if (item.Event.Type != MatchEventType.Goal) continue;
            if (item.TeamId == fixture.Home.Id) home++;
            else if (item.TeamId == fixture.Away.Id) away++;
            else Assert.Fail("A goal must belong to one of the fixture teams.");
        }
        Assert.Equal((1, 1), (home, away));

        var beforeHomeSubstitution = fixture.Frames.Single(f => f.ClockSeconds == 3570).Players;
        var afterHomeSubstitution = fixture.Frames.Single(f => f.ClockSeconds == 3600).Players;
        Assert.Contains(beforeHomeSubstitution, p => p.PlayerId == "harbour-7");
        Assert.DoesNotContain(beforeHomeSubstitution, p => p.PlayerId == "harbour-12");
        Assert.DoesNotContain(afterHomeSubstitution, p => p.PlayerId == "harbour-7");
        Assert.Contains(afterHomeSubstitution, p => p.PlayerId == "harbour-12");

        var beforeAwaySubstitution = fixture.Frames.Single(f => f.ClockSeconds == 4080).Players;
        var afterAwaySubstitution = fixture.Frames.Single(f => f.ClockSeconds == 4100).Players;
        Assert.Contains(beforeAwaySubstitution, p => p.PlayerId == "ridge-9");
        Assert.DoesNotContain(beforeAwaySubstitution, p => p.PlayerId == "ridge-12");
        Assert.DoesNotContain(afterAwaySubstitution, p => p.PlayerId == "ridge-9");
        Assert.Contains(afterAwaySubstitution, p => p.PlayerId == "ridge-12");
    }

    [Fact] public void DefinitionRejectsEventsWhosePeriodConflictsWithMatchClock()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition["scenarioEvents"]![0]!["period"] = 2;

        var failure = Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
        Assert.Contains("period does not match its match clock", failure.Message);
    }

    [Fact] public void DefinitionRejectsDuplicateTerminalMarkers()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        var scenarioEvents = definition["scenarioEvents"]!.AsArray();
        scenarioEvents.Add(scenarioEvents[scenarioEvents.Count - 1]!.DeepClone());

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsExtraKickoffOutsideThePeriodBoundaries()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        var extraKickoff = definition["scenarioEvents"]![0]!.DeepClone();
        extraKickoff["clockSeconds"] = 100;
        definition["scenarioEvents"]!.AsArray().Add(extraKickoff);

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsMissingStartTimeAndNullCollections()
    {
        var missingStart = JsonNode.Parse(ReadDefinition())!.AsObject();
        missingStart.Remove("startUtc");
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(missingStart.ToJsonString()));

        var nullTeams = JsonNode.Parse(ReadDefinition())!;
        nullTeams["teams"] = null;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(nullTeams.ToJsonString()));

        var nullPlayer = JsonNode.Parse(ReadDefinition())!;
        nullPlayer["teams"]![0]!["players"]![0] = null;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(nullPlayer.ToJsonString()));
    }

    [Theory]
    [InlineData("pitchLengthMetres", 89)]
    [InlineData("pitchLengthMetres", 121)]
    [InlineData("pitchWidthMetres", 44)]
    [InlineData("pitchWidthMetres", 91)]
    public void DefinitionRejectsDimensionsOutsideFootballPitchRanges(string property, double value)
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition[property] = value;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsFrameOverrideForInactivePlayer()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition["frameOverrides"]![0]!["playerPositions"]!["harbour-12"] = new JsonArray(42, 24);

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    private static string ReadDefinition()
    {
        using var stream = typeof(SyntheticFixtureGenerator).Assembly
            .GetManifestResourceStream("PitchMind.Domain.Simulation.synthetic-fixture-v2.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Fingerprint(SyntheticFixture fixture)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fixture));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Fingerprint(MatchState state)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static MatchAggregate Replay(SyntheticFixture fixture, IEnumerable<SimulationEvent> events)
    {
        var players = fixture.Home.Players.Concat(fixture.Away.Players).Select(player => (player.Id, player.TeamId));
        var activePlayers = fixture.Frames.Single(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId);
        var aggregate = new MatchAggregate(new(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id,
            players, activePlayers, fixture.Provenance);
        foreach (var item in events) aggregate.Apply(item);
        return aggregate;
    }
}
