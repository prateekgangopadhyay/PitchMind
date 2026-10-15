using PitchMind.Domain.Match;
using System.Text.Json;
using PitchMind.Application.Matches;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Events;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using Xunit;
namespace PitchMind.Application.Tests;
public sealed class SyntheticMatchTests
{
    [Fact] public void ReplayExcludesFutureGoalsAndEvidence()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        Assert.Equal(0, service.GetState(3308).HomeScore); Assert.Empty(service.GetInsights(3308));
        Assert.Equal(1, service.GetState(3309).HomeScore); Assert.Single(service.GetInsights(3309));
        Assert.Equal(0, service.GetState(4679).AwayScore); Assert.Equal(1, service.GetState(4680).AwayScore);
        var insight = service.GetInsights(5400).Single();
        Assert.Equal("simulated", insight.Classification);
        Assert.Equal("press-sequence-v3", insight.DetectorVersion);
        Assert.Equal("unapproved-demo", insight.PolicyStatus);
        Assert.Equal("synthetic", insight.Provenance);
        Assert.Matches("^[0-9a-f]{64}$", insight.SourceSha256);
        Assert.False(insight.Publishable);
        Assert.Null(insight.CanonicalRevision);
        Assert.Equal(5400, insight.SelectedTimeSeconds);
        Assert.Equal(service.GetEvents(5400)[^1].Event.Sequence, insight.ThroughSequence);
        Assert.Equal(service.Fixture.Version, insight.SourceVersion);
        Assert.Contains(insight.Conditions, condition => condition.Contains("policy: press-sequence-v3", StringComparison.Ordinal));
        var graph = insight.EvidenceGraph!;
        Assert.Equal(insight.MatchId, graph.MatchId);
        Assert.Equal(insight.SourceVersion, graph.SourceVersion);
        Assert.Equal(insight.SourceSha256, graph.SourceSha256);
        Assert.Null(graph.CanonicalRevision);
        Assert.False(graph.Publishable);
        Assert.Equal("synthetic", graph.Provenance);
        Assert.All(graph.Nodes, node => Assert.Equal(insight.SourceSha256, node.SourceSha256));
        Assert.All(graph.Conditions.SelectMany(condition => condition.SourceNodeIds), sourceNodeId =>
            Assert.Contains(graph.Nodes, node => node.Id == sourceNodeId));
        Assert.False(graph.Conditions.Single(condition => condition.Id == "publication-policy").Passed);
        Assert.NotEmpty(graph.Uncertainty);
        Assert.Equal("pattern", graph.Nodes.Single(node => node.Id == graph.ClaimNodeId).TruthClass);
        Assert.All(insight.EventIds, eventId => Assert.Contains(graph.Nodes, node => node.EventId == eventId));
        Assert.Equal(insight.FrameIds.Order(), graph.Nodes.Where(node => node.FrameId is not null).Select(node => node.FrameId!).Order());
        var contextNode = Assert.Single(graph.Nodes, node => node.Id == $"metric:{insight.Id}:selected-context");
        var activeRosterNode = Assert.Single(graph.Nodes, node => node.Id == $"metric:{insight.Id}:active-roster");
        Assert.Equal("fact", activeRosterNode.TruthClass);
        Assert.Contains(graph.Edges, edge => edge.FromNodeId == contextNode.Id && edge.ToNodeId == activeRosterNode.Id && edge.Relation == "derived_from");
        Assert.Equal("derivation", contextNode.TruthClass);
        Assert.Equal(0m, contextNode.NumericValue);
        Assert.Equal("active-players", contextNode.Unit);
        Assert.Contains(graph.Edges, edge => edge.FromNodeId == graph.ClaimNodeId && edge.ToNodeId == contextNode.Id && edge.Relation == "derived_from");
        var selectedContextCondition = Assert.Single(graph.Conditions, condition => condition.Id == "selected-context");
        Assert.Contains(activeRosterNode.Id, selectedContextCondition.SourceNodeIds);
        Assert.All(graph.Edges.Where(edge => edge.FromNodeId == contextNode.Id && edge.ToNodeId.StartsWith("event:", StringComparison.Ordinal)),
            edge => Assert.Contains(edge.ToNodeId, selectedContextCondition.SourceNodeIds));
        Assert.All(graph.Edges, edge => Assert.Contains(edge.FromNodeId, graph.Nodes.Select(node => node.Id)));
        Assert.All(graph.Edges, edge => Assert.Contains(edge.ToNodeId, graph.Nodes.Select(node => node.Id)));
        Assert.Equal(graph.Nodes.Count(node => node.Kind == "event") - 1, graph.Edges.Count(edge => edge.Relation == "precedes"));
        Assert.Equal(insight.EventIds.Count + insight.FrameIds.Count + 1 + graph.Edges.Count(edge => edge.FromNodeId == contextNode.Id),
            graph.Edges.Count(edge => edge.Relation == "derived_from"));
        Assert.Null(service.GetEvidence(insight.Id, 3308));
        Assert.All(service.GetEvidence(insight.Id, 3309)!.Events, e => Assert.True(e.Event.ClockSeconds <= 3309));
    }
    [Fact] public void CompleteSyntheticScenarioReplaysScoreTransitionsWithoutLookahead()
    {
        var scenario = SyntheticFixtureGenerator.GenerateScenario(seed: 42);
        var service = new SyntheticMatchService(scenario.Fixture);

        Assert.Equal((0, 0), (service.GetState(3308).HomeScore, service.GetState(3308).AwayScore));
        Assert.Equal((1, 0), (service.GetState(3309).HomeScore, service.GetState(3309).AwayScore));
        Assert.Equal((1, 0), (service.GetState(4679).HomeScore, service.GetState(4679).AwayScore));
        Assert.Equal((1, 1), (service.GetState(4680).HomeScore, service.GetState(4680).AwayScore));
        Assert.Equal((1, 1), (service.GetState(5400).HomeScore, service.GetState(5400).AwayScore));
    }
    [Fact] public void SameObservedSequenceGetsSelectedTimeContextWithoutTacticalJudgment()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var baseline = new SyntheticMatchService(fixture).GetInsights(3309).Single();
        var later = new SyntheticMatchService(fixture).GetInsights(4680).Single();
        var awayCirculation = fixture.Events.First(item => item.Action == "circulation" &&
            item.TeamId == fixture.Away.Id && item.Event.ClockSeconds > 2700 && item.Event.ClockSeconds < 3309);
        var contextChangedEvents = fixture.Events.Select(item => item.Event.EventId == awayCirculation.Event.EventId ? item with
        {
            Event = item.Event with { Type = PitchMind.Domain.Events.MatchEventType.Goal },
            Action = "goal",
            Outcome = "scored"
        } : item).ToArray();
        var contextChanged = new SyntheticMatchService(fixture with { Events = contextChangedEvents })
            .GetInsights(3309).Single();

        Assert.Equal(baseline.EventIds, contextChanged.EventIds);
        Assert.Equal(baseline.FrameIds, contextChanged.FrameIds);
        Assert.Equal(baseline.EventIds, later.EventIds);
        Assert.Equal(baseline.FrameIds, later.FrameIds);
        Assert.Equal("tactical-context-v1", baseline.Context!.SchemaVersion);
        Assert.Equal("Harbour FC 1–0 Ridge Athletic", baseline.Context.Scoreline);
        Assert.Equal("Harbour FC 1–1 Ridge Athletic", contextChanged.Context!.Scoreline);
        Assert.Equal("Harbour FC 1–1 Ridge Athletic", later.Context!.Scoreline);
        Assert.DoesNotContain("1–1", baseline.Context.Scoreline, StringComparison.Ordinal);
        Assert.Equal("home", baseline.Context.TeamRole);
        Assert.Equal(0, baseline.Context.NumericalAdvantage);
        Assert.Equal("SecondHalf", baseline.Context.MatchPhase);
        Assert.Equal("opponent in possession at trigger; defending after observed regain", baseline.Context.OpponentPhase);
        Assert.Contains("scoreline is Harbour FC 1–0 Ridge Athletic", baseline.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("superior", baseline.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("low block", baseline.Summary, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void VersionPinnedReplayIsDeterministicAndContainsOnlySelectedTimeOutputs()
    {
        var first = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).ReplayAt(3308);
        var repeated = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).ReplayAt(3308, first.FixtureVersion, first.FixtureSeed,
            first.ReplayAlgorithmVersion, first.DetectorVersion, first.ReplayInputSha256);
        var afterGoal = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).ReplayAt(3309, first.FixtureVersion, first.FixtureSeed,
            first.ReplayAlgorithmVersion, first.DetectorVersion, first.ReplayInputSha256);

        Assert.Equal("offline-synthetic-fixture", first.ExecutionMode);
        Assert.Null(first.CanonicalRevision);
        Assert.Equal(first.ReplayInputSha256, repeated.ReplayInputSha256);
        Assert.Equal(first.ReplayOutputSha256, repeated.ReplayOutputSha256);
        Assert.NotEqual(first.ReplayOutputSha256, afterGoal.ReplayOutputSha256);
        Assert.Equal(0, first.State.HomeScore);
        Assert.Empty(first.Insights);
        Assert.DoesNotContain(first.Events, item => item.Event.EventId == afterGoal.Events.Single(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Goal).Event.EventId);
        Assert.All(first.Events, item => Assert.True(item.Event.ClockSeconds <= first.SelectedElapsedSeconds));
        Assert.All(first.Insights, item => Assert.True(item.ClockSeconds <= first.SelectedElapsedSeconds));
        Assert.True(first.State.Frame.ClockSeconds <= first.SelectedElapsedSeconds);
        Assert.Equal(1, afterGoal.State.HomeScore);
        Assert.Single(afterGoal.Insights);
    }
    [Fact] public void VersionPinnedReplayRejectsUnavailableInputsAndAlgorithms()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());

        Assert.Throws<ArgumentException>(() => service.ReplayAt(3308, fixtureVersion: "unknown"));
        Assert.Throws<ArgumentException>(() => service.ReplayAt(3308, fixtureSeed: 41));
        Assert.Throws<ArgumentException>(() => service.ReplayAt(3308, replayAlgorithmVersion: "unknown"));
        Assert.Throws<ArgumentException>(() => service.ReplayAt(3308, detectorVersion: "unknown"));
        Assert.Throws<ArgumentException>(() => service.ReplayAt(3308, replayInputSha256: new string('0', 64)));
    }
    [Fact] public void CandidateReplayGoldenSnapshotsPinSelectedTimeFootballState()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ep04-s05-synthetic-v3-replay-goldens.json");
        var golden = JsonSerializer.Deserialize<ReplayGoldenDocument>(File.ReadAllText(goldenPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("candidate-regression-goldens-pending-product-owner-review", golden.Approval);
        var service = new SyntheticMatchService(GenerateReplayGoldenFixture());
        foreach (var expected in golden.Snapshots)
        {
            var replay = service.ReplayAt(expected.SelectedElapsedSeconds, golden.FixtureVersion, golden.FixtureSeed,
                SyntheticMatchService.ReplayAlgorithmVersion, PressSequencePolicy.UnapprovedDemoV3.Version, golden.FixtureContentSha256);
            Assert.Equal(expected.ReplayOutputSha256, replay.ReplayOutputSha256);
            Assert.Equal(expected.LastSequence, replay.State.LastSequence);
            Assert.Equal(expected.Period, replay.State.Period);
            Assert.Equal(expected.Phase, replay.State.Phase.ToString().ToLowerInvariant());
            Assert.Equal(expected.HomeScore, replay.State.HomeScore);
            Assert.Equal(expected.AwayScore, replay.State.AwayScore);
            Assert.Equal(expected.YellowCards, replay.State.Players.Sum(player => player.YellowCards));
            Assert.Equal(expected.HomeActivePlayers, replay.State.HomeActivePlayers);
            Assert.Equal(expected.AwayActivePlayers, replay.State.AwayActivePlayers);
            Assert.Equal(expected.InsightCount, replay.Insights.Count);
        }
    }
    [Fact] public void ReplayInputPinRejectsChangedCompetitionRuleContext()
    {
        var fixture = SyntheticFixtureGenerator.GenerateScenario(seed: 42).Fixture with
        {
            CompetitionId = "synthetic-competition",
            CompetitionRulesVersion = "rules-v1",
            MaximumSubstituteReentriesPerPlayer = 0
        };
        var original = new SyntheticMatchService(fixture).ReplayAt(3600);
        var correctedRules = fixture with { CompetitionRulesVersion = "rules-v2" };

        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(correctedRules).ReplayAt(3600,
            replayInputSha256: original.ReplayInputSha256));
    }

    [Fact] public void CorrectedFixtureInputKeepsEventIdentityAndDoesNotMutatePriorReplayVersion()
    {
        var source = SyntheticFixtureGenerator.Generate();
        var originalService = new SyntheticMatchService(source);
        var originalReplay = originalService.ReplayAt(3309);
        var goal = source.Events.Single(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Goal &&
            item.Event.ClockSeconds == 3309);
        var awayPlayer = source.Frames.Single(frame => frame.ClockSeconds == 0).Players
            .First(player => player.TeamId == source.Away.Id).PlayerId;
        var correctedEvents = source.Events.Select(item => item.Event.EventId == goal.Event.EventId
            ? item with { TeamId = source.Away.Id, PlayerId = awayPlayer }
            : item).ToArray();
        var correctedFixture = source with
        {
            Version = "synthetic-v3-correction-1",
            Events = Array.AsReadOnly(correctedEvents)
        };

        var correctedReplay = new SyntheticMatchService(correctedFixture).ReplayAt(3309);
        var correctedGoal = correctedReplay.Events.Single(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Goal);

        Assert.Equal(goal.Event.EventId, correctedGoal.Event.EventId);
        Assert.Equal(goal.Event.Sequence, correctedGoal.Event.Sequence);
        Assert.Equal(0, correctedReplay.State.HomeScore);
        Assert.Equal(1, correctedReplay.State.AwayScore);
        Assert.NotEqual(originalReplay.FixtureVersion, correctedReplay.FixtureVersion);
        Assert.NotEqual(originalReplay.ReplayInputSha256, correctedReplay.ReplayInputSha256);
        Assert.NotEqual(originalReplay.ReplayOutputSha256, correctedReplay.ReplayOutputSha256);
        Assert.Equal(originalReplay.ReplayOutputSha256, originalService.ReplayAt(3309).ReplayOutputSha256);
    }
    [Fact] public void SelectedTimeProjectsCanonicalCardsRosterPossessionAndPhase()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.GenerateScenario(seed: 42).Fixture);
        var beforeSubstitution = service.GetState(3599);
        var afterSubstitution = service.GetState(3600);
        var firstHalf = service.GetState(2699);
        var secondHalf = service.GetState(2700);

        Assert.Equal("replay", firstHalf.Status);
        Assert.Equal("replay", secondHalf.Status);
        Assert.Equal("FirstHalf", firstHalf.Phase.ToString());
        Assert.Equal("SecondHalf", secondHalf.Phase.ToString());
        Assert.Equal(1, beforeSubstitution.Players.Single(player => player.PlayerId == "ridge-4").YellowCards);
        Assert.False(beforeSubstitution.Players.Single(player => player.PlayerId == "harbour-12").Active);
        Assert.False(afterSubstitution.Players.Single(player => player.PlayerId == "harbour-7").Active);
        Assert.True(afterSubstitution.Players.Single(player => player.PlayerId == "harbour-12").Active);
        Assert.Equal(11, afterSubstitution.HomeActivePlayers);
        Assert.Equal("harbour", afterSubstitution.PossessionTeamId);
    }
    [Fact] public void MissingTrackingSuppressesSpatialClaim()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        // Keep the required kickoff sample for roster initialization, but remove all
        // event-time tracking needed to support a spatial pressure sequence.
        var service = new SyntheticMatchService(fixture with
        {
            Frames = fixture.Frames.Where(frame => frame.ClockSeconds == 0).ToArray()
        });
        Assert.Empty(service.GetInsights(5400));
        Assert.Equal(0, service.GetState(5400).Frame.ClockSeconds);
    }
    [Fact] public void FixtureWithoutKickoffFrameIsRejectedAtConstruction()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Where(frame => frame.ClockSeconds != 0).ToArray();

        var error = Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));

        Assert.Contains("tracking frame at kickoff", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void FixtureWhoseFirstSampleIsAfterSelectedTimeIsRejectedBeforeQuery()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Where(frame => frame.ClockSeconds >= 60).ToArray();

        var error = Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));

        Assert.Contains("elapsed second zero", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void KeyMomentsPrioritizeRecordedGoalsAndAvoidPassSpamWithoutLookingAhead()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        Assert.DoesNotContain(service.GetKeyMoments(3308), moment => moment.Kind == "goal");

        var afterGoal = service.GetKeyMoments(3309);
        var goal = Assert.Single(afterGoal, moment => moment.Kind == "goal");
        Assert.Equal(3309, goal.SelectedTimeSeconds);
        Assert.Equal("fact", goal.TruthClass);
        Assert.False(goal.Publishable);
        Assert.Equal("unapproved-demo", goal.PolicyStatus);
        Assert.Single(goal.EventIds);
        Assert.DoesNotContain(afterGoal, moment => moment.Kind == "pass");
        Assert.All(afterGoal, moment => Assert.True(moment.ClockSeconds <= moment.SelectedTimeSeconds));
    }

    [Fact] public void KeyMomentIdsAreStableAndTacticalCandidatesRetainTheirWhyEvidence()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var first = service.GetKeyMoments(5400);
        var second = service.GetKeyMoments(5400);
        Assert.Equal(first.Select(moment => moment.Id), second.Select(moment => moment.Id));

        var tactical = Assert.Single(first, moment => moment.Kind == "tactical-pattern");
        Assert.Equal("pattern", tactical.TruthClass);
        Assert.NotEmpty(tactical.EventIds);
        Assert.NotEmpty(tactical.FrameIds);
        Assert.NotEmpty(tactical.EvidenceClaimNodeIds);
        var sourceOrderedEvidenceIds = service.GetEvents(5400)
            .Where(item => tactical.EventIds.Contains(item.Event.EventId))
            .Select(item => item.Event.EventId);
        Assert.Equal(sourceOrderedEvidenceIds, tactical.EventIds);
        Assert.Equal("key-moment-v1-demo", tactical.PolicyVersion);
        Assert.False(tactical.Publishable);
    }

    [Fact] public void KeyMomentsCarryExactSourceHashAndRejectInsightsFromAnotherSource()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var fixture = service.Fixture;
        var sourceSha256 = PressSequenceDetector.ComputeSourceSha256(fixture);
        var moments = service.GetKeyMoments(5400);

        Assert.All(moments, moment => Assert.Equal(sourceSha256, moment.SourceSha256));
        var insight = service.GetInsights(5400).Single();
        var mismatchedInsight = insight with { SourceSha256 = new string('a', 64) };
        var generated = KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version, sourceSha256, fixture.Provenance,
            null, 5400, fixture.Events[^1].Event.Sequence, fixture.Home.Id, fixture.Home.Name,
            fixture.Away.Id, fixture.Away.Name, fixture.Events, [mismatchedInsight]);
        Assert.DoesNotContain(generated, moment => moment.Kind == "tactical-pattern");

        var firstSource = KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version, sourceSha256, fixture.Provenance,
            null, 3309, service.GetEvents(3309)[^1].Event.Sequence, fixture.Home.Id, fixture.Home.Name,
            fixture.Away.Id, fixture.Away.Name, service.GetEvents(3309), Array.Empty<TacticalInsight>())
            .Single(moment => moment.Kind == "goal");
        var alternateSource = KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version, new string('b', 64), fixture.Provenance,
            null, 3309, service.GetEvents(3309)[^1].Event.Sequence, fixture.Home.Id, fixture.Home.Name,
            fixture.Away.Id, fixture.Away.Name, service.GetEvents(3309), Array.Empty<TacticalInsight>())
            .Single(moment => moment.Kind == "goal");
        Assert.NotEqual(firstSource.Id, alternateSource.Id);
    }

    [Fact] public void KeyMomentDecayAndThresholdCanSuppressStaleItems()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var policy = KeyMomentPolicy.UnapprovedDemoV1 with { RecencyHalfLifeSeconds = 1, MinimumRelevance = 99.9m };
        var moments = service.GetKeyMoments(5400, policy);
        Assert.DoesNotContain(moments, moment => moment.Kind == "goal");
    }

    [Fact] public void RepeatedShotsWithinTheDeduplicationWindowShareOneMomentAndEvidencePath()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var fixture = service.Fixture;
        var last = fixture.Events[^1].Event;
        var firstDuplicateId = Guid.NewGuid();
        var secondDuplicateId = Guid.NewGuid();
        var first = new SimulationEvent(MatchEvent.Create(firstDuplicateId, last.MatchId, last.Sequence + 1,
            MatchEventType.Shot, 5400, 2, last.OccurredAt.AddSeconds(1)), fixture.Home.Id, "", "shot", "saved");
        var second = new SimulationEvent(MatchEvent.Create(secondDuplicateId, last.MatchId, last.Sequence + 2,
            MatchEventType.Shot, 5400, 2, last.OccurredAt.AddSeconds(2)), fixture.Home.Id, "", "shot", "blocked");
        var events = fixture.Events.Concat([first, second]).ToArray();

        var sourceSha256 = PressSequenceDetector.ComputeSourceSha256(fixture);
        var moments = KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version, sourceSha256, fixture.Provenance, null, 5400,
            second.Event.Sequence, fixture.Home.Id, fixture.Home.Name, fixture.Away.Id, fixture.Away.Name,
            events, Array.Empty<TacticalInsight>());

        var shotMoment = Assert.Single(moments, moment => moment.EventIds.Contains(firstDuplicateId));
        Assert.Contains(secondDuplicateId, shotMoment.EventIds);
        Assert.Equal("fact", shotMoment.TruthClass);
    }

    [Fact] public void ShotDeduplicationDoesNotCrossPeriodBoundaries()
    {
        var matchId = Guid.NewGuid();
        SimulationEvent Event(long sequence, int clock, int period) => new(
            MatchEvent.Create(Guid.NewGuid(), new(matchId), sequence, MatchEventType.Shot, clock, period, DateTimeOffset.UnixEpoch.AddSeconds(clock)),
            "home", "", "shot", "saved");
        var moments = KeyMomentGenerator.Generate(matchId.ToString(), "fixture-v1", new string('a', 64), "synthetic", null, 2701, 1,
            "home", "Home", "away", "Away", [Event(0, 2699, 1), Event(1, 2701, 2)], Array.Empty<TacticalInsight>());

        Assert.Equal(2, moments.Count(moment => moment.Kind == "shot"));
    }

    [Fact] public void ShotDeduplicationUsesAStableSlidingWindowAtItsBoundary()
    {
        var matchId = Guid.NewGuid();
        SimulationEvent Event(long sequence, int clock) => new(
            MatchEvent.Create(Guid.NewGuid(), new(matchId), sequence, MatchEventType.Shot, clock, 1, DateTimeOffset.UnixEpoch.AddSeconds(clock)),
            "home", "", "shot", "saved");
        var events = new[] { Event(0, 100), Event(1, 145), Event(2, 191) };
        var moments = KeyMomentGenerator.Generate(matchId.ToString(), "fixture-v1", new string('a', 64), "synthetic", null, 191, 2,
            "home", "Home", "away", "Away", events, Array.Empty<TacticalInsight>());

        Assert.Equal(2, moments.Count(moment => moment.Kind == "shot"));
        Assert.Contains(moments, moment => moment.EventIds.Count == 2 && moment.EventIds.Contains(events[0].Event.EventId) && moment.EventIds.Contains(events[1].Event.EventId));
        Assert.Contains(moments, moment => moment.EventIds.SequenceEqual([events[2].Event.EventId]));
    }

    [Fact] public void KeyMomentGeneratorRejectsEventsForUnknownTeams()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var fixture = service.Fixture;
        var events = fixture.Events.ToArray();
        events[0] = events[0] with { TeamId = "unmapped-team" };

        Assert.Throws<ArgumentException>(() => KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version,
            PressSequenceDetector.ComputeSourceSha256(fixture), fixture.Provenance, null, 5400,
            fixture.Events[^1].Event.Sequence, fixture.Home.Id, fixture.Home.Name, fixture.Away.Id,
            fixture.Away.Name, events, Array.Empty<TacticalInsight>()));
    }

    [Fact] public void KeyMomentGeneratorRejectsAnIncorrectSequenceWatermark()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var fixture = service.Fixture;
        Assert.Throws<ArgumentException>(() => KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version,
            PressSequenceDetector.ComputeSourceSha256(fixture), fixture.Provenance, null, 5400, fixture.Events[^1].Event.Sequence - 1, fixture.Home.Id, fixture.Home.Name,
            fixture.Away.Id, fixture.Away.Name, fixture.Events, Array.Empty<TacticalInsight>()));
    }

    [Fact] public void KeyMomentGeneratorRejectsGapsInTheSelectedTimeSourcePrefix()
    {
        var matchId = Guid.NewGuid();
        SimulationEvent Event(long sequence, int clock) => new(
            MatchEvent.Create(Guid.NewGuid(), new(matchId), sequence, MatchEventType.Pass, clock, 1, DateTimeOffset.UnixEpoch.AddSeconds(clock)),
            "home", "player", "pass", "complete");
        var events = new[] { Event(0, 10), Event(2, 20) };

        Assert.Throws<ArgumentException>(() => KeyMomentGenerator.Generate(matchId.ToString(), "fixture-v1", new string('a', 64), "synthetic", null,
            20, 2, "home", "Home", "away", "Away", events, Array.Empty<TacticalInsight>()));
    }

    [Fact] public void GoldenReplayCatalogHasVersionedPositiveAndNegativeDetectionOutcomes()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "press-sequence-goldens-v1.json")));
        var root = catalog.RootElement;
        Assert.Equal("press-sequence-goldens-v1", root.GetProperty("catalogVersion").GetString());
        Assert.Equal(SyntheticFixtureGenerator.Version, root.GetProperty("sourceFixtureVersion").GetString());
        Assert.Equal(PressSequencePolicy.UnapprovedDemoV3.Version, root.GetProperty("detectorVersion").GetString());
        Assert.Equal("synthetic", root.GetProperty("provenance").GetString());

        var snapshot = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).GetState(5400);
        var selectedState = new MatchState(snapshot.MatchId, snapshot.ClockSeconds, snapshot.Period,
            snapshot.Phase, snapshot.HomeScore, snapshot.AwayScore, snapshot.PossessionTeamId,
            snapshot.HomeActivePlayers, snapshot.AwayActivePlayers, snapshot.Players, snapshot.LastSequence, snapshot.Provenance);
        foreach (var golden in root.GetProperty("cases").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(golden.GetProperty("rationale").GetString()));
            var fixture = SyntheticFixtureGenerator.Generate();
            var mutation = golden.GetProperty("mutation").GetString();
            if (mutation == "complete-response")
                fixture = fixture with { Events = fixture.Events.Select(item => item.Action == "pressured-pass" ? item with { Outcome = "complete" } : item).ToArray() };
            else if (mutation == "wrong-regain-team")
                fixture = fixture with { Events = fixture.Events.Select(item => item.Action == "high-regain" ? item with { TeamId = fixture.Away.Id } : item).ToArray() };
            else if (mutation == "missing-pressure-frame")
                fixture = fixture with { Frames = fixture.Frames.Where(frame => frame.ClockSeconds != 3293).ToArray() };
            else if (mutation == "missing-pressure-presser")
                fixture = fixture with { Frames = fixture.Frames.Select(frame => frame.ClockSeconds == 3293
                    ? frame with { Players = frame.Players.Where(player => player.PlayerId != "harbour-10").ToArray() }
                    : frame).ToArray() };
            else if (mutation == "response-gap-exceeds-policy")
                fixture = fixture with { Events = fixture.Events.Select(item => item.Action switch
                {
                    "pressured-pass" => item with { Event = item.Event with { ClockSeconds = 3304, OccurredAt = item.Event.OccurredAt.AddSeconds(8) } },
                    "high-regain" => item with { Event = item.Event with { ClockSeconds = 3305, OccurredAt = item.Event.OccurredAt.AddSeconds(6) } },
                    "cutback" => item with { Event = item.Event with { ClockSeconds = 3308, OccurredAt = item.Event.OccurredAt.AddSeconds(4) } },
                    "shot" => item with { Event = item.Event with { ClockSeconds = 3312, OccurredAt = item.Event.OccurredAt.AddSeconds(4) } },
                    "goal" when item.Event.ClockSeconds == 3309 => item with { Event = item.Event with { ClockSeconds = 3313, OccurredAt = item.Event.OccurredAt.AddSeconds(4) } },
                    _ => item
                }).ToArray() };
            else if (mutation == "duplicate-source-event-id")
            {
                var originalIndex = fixture.Events.ToList().FindIndex(item => item.Action == "press-arrival");
                var items = fixture.Events.ToList();
                items.Insert(originalIndex + 1, items[originalIndex] with
                {
                    Event = items[originalIndex].Event with { Sequence = items[originalIndex].Event.Sequence + 1 }
                });
                fixture = fixture with { Events = items.Select((item, index) => item with
                    { Event = item.Event with { Sequence = index } }).ToArray() };
            }
            else if (mutation == "pressure-period-boundary")
                fixture = fixture with { Events = fixture.Events.Select(item => item.Action == "press-arrival" ? item with { Event = item.Event with { Period = 1 } } : item).ToArray() };
            else Assert.Equal("none", mutation);

            var input = new PressSequenceInput(fixture.MatchId, fixture.Version, fixture.Provenance, null, 5400,
                fixture.Events[^1].Event.Sequence, fixture.Home.Id, fixture.Home.Name, fixture.Away.Id, fixture.Away.Name,
                "stadium-metres", fixture.PitchLengthMetres, fixture.PitchWidthMetres, fixture.Events, fixture.Frames,
                PressSequenceDetector.ComputeSourceSha256(fixture))
            {
                SelectedState = selectedState
            };
            if (golden.TryGetProperty("expectedFailure", out var expectedFailure))
            {
                Assert.Equal("duplicate-source-event-id", expectedFailure.GetString());
                Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(input));
                continue;
            }

            var insights = PressSequenceDetector.Detect(input);
            Assert.Equal(golden.GetProperty("expectedInsightCount").GetInt32(), insights.Count);
            Assert.All(insights, insight =>
            {
                Assert.Equal("simulated", insight.Classification);
                Assert.Equal("synthetic", insight.EvidenceGraph!.Provenance);
                Assert.Equal("unapproved-demo", insight.PolicyStatus);
                Assert.False(insight.Publishable);
                Assert.False(insight.EvidenceGraph.Publishable);
            });
        }
    }

    [Fact] public void MechanismReplayPreservesSourceTimeAndPeriodSemantics()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insight = service.GetInsights(5400).Single();
        var replay = service.GetMechanismReplay(insight.Id, 5400)!;

        Assert.Equal("synthetic", replay.Provenance);
        Assert.Null(replay.CanonicalRevision);
        Assert.False(replay.Publishable);
        Assert.Matches("^[0-9a-f]{64}$", replay.SourceSha256);
        Assert.All(replay.Events, item =>
        {
            var source = service.Fixture.Events.Single(candidate => candidate.Event.EventId == item.Event.EventId);
            Assert.Equal(source.Event.Sequence, item.Event.Sequence);
            Assert.Equal(source.Event.ClockSeconds, item.Event.ClockSeconds);
            Assert.Equal(source.Event.Period, item.Event.Period);
            Assert.Equal(source.Event.OccurredAt, item.Event.OccurredAt);
        });
        Assert.All(replay.Frames, item => Assert.Equal(service.Fixture.Frames.Single(frame => frame.Id == item.Id), item));
    }

    [Fact] public void MirroringPitchCoordinatesPreservesTheSameMechanismAndEvidenceIdentity()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var expected = new SyntheticMatchService(fixture).GetInsights(5400).Single();
        var mirrored = fixture with
        {
            Frames = fixture.Frames.Select(frame => frame with
            {
                BallX = fixture.PitchLengthMetres - frame.BallX,
                Players = frame.Players.Select(player => player with { X = fixture.PitchLengthMetres - player.X }).ToArray()
            }).ToArray()
        };
        var actual = new SyntheticMatchService(mirrored).GetInsights(5400).Single();

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.EventIds, actual.EventIds);
        Assert.Equal(expected.FrameIds, actual.FrameIds);
        Assert.Equal(expected.Conditions, actual.Conditions);
    }

    [Fact] public void MechanismReplayExportsOnlySelectedEvidenceAndStableParticipantPositions()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insight = service.GetInsights(3309).Single();
        var first = service.GetMechanismReplay(insight.Id, 3309)!;
        var second = service.GetMechanismReplay(insight.Id, 3309)!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(insight.EventIds, first.MechanismEventIds);
        Assert.Equal(first.EvidenceGraph.Nodes.Where(node => node.Kind == "event").Select(node => node.EventId).Order(), first.EventIds.Order().Select(id => (Guid?)id));
        Assert.Equal(insight.FrameIds, first.FrameIds);
        Assert.Equal(insight.EvidenceGraph!.ClaimNodeId, first.EvidenceGraph.ClaimNodeId);
        Assert.Equal(insight.EvidenceGraph.Nodes.Select(node => node.Id), first.EvidenceGraph.Nodes.Select(node => node.Id));
        Assert.Equal(insight.EvidenceGraph.Edges, first.EvidenceGraph.Edges);
        Assert.Equal(3309, first.SelectedTimeSeconds);
        Assert.All(first.Events, item => Assert.True(item.Event.ClockSeconds <= first.SelectedTimeSeconds));
        Assert.All(first.Frames, frame => Assert.True(frame.ClockSeconds <= first.SelectedTimeSeconds));
        Assert.Contains(first.Events.Select(item => item.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)), id => first.ParticipantIds.Contains(id));
        Assert.Contains(first.Frames.SelectMany(frame => frame.Players), position => first.ParticipantIds.Contains(position.PlayerId));
        Assert.False(first.Publishable);
    }

    [Fact] public void ReplayIsUnavailableBeforeTheCompleteEvidenceExists()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insightId = service.GetInsights(3309).Single().Id;
        Assert.Null(service.GetMechanismReplay(insightId, 3308));
        Assert.Null(service.GetMechanismReplay("unknown", 5400));
    }

    [Fact] public void MechanismReplayRejectsEventPayloadThatDisagreesWithGraphClock()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insight = service.GetInsights(3309).Single();
        var evidence = service.GetEvidence(insight.Id, 3309)!;
        var first = evidence.Events[0];
        var tampered = evidence with
        {
            Events = evidence.Events.Select((item, index) => index == 0
                ? item with { Event = item.Event with { ClockSeconds = item.Event.ClockSeconds + 1 } }
                : item).ToArray()
        };

        Assert.NotEqual(first.Event.ClockSeconds, tampered.Events[0].Event.ClockSeconds);
        Assert.Throws<InvalidDataException>(() => MechanismReplayBuilder.Build(tampered, insight.ThroughSequence));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("frame")]
    public void MechanismReplayRejectsDuplicateGraphSourceReferences(string kind)
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insight = service.GetInsights(3309).Single();
        var evidence = service.GetEvidence(insight.Id, 3309)!;
        var graph = evidence.Insight.EvidenceGraph!;
        var sourceNode = graph.Nodes.First(node => node.Kind == kind);
        var duplicateNode = sourceNode with { Id = sourceNode.Id + ":duplicate" };
        var tampered = evidence with
        {
            Insight = evidence.Insight with
            {
                EvidenceGraph = graph with { Nodes = graph.Nodes.Append(duplicateNode).ToArray() }
            }
        };

        Assert.Throws<InvalidDataException>(() => MechanismReplayBuilder.Build(tampered, insight.ThroughSequence));
    }

    [Fact] public void SuccessfulCirculationDoesNotBecomePressingTrap()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "pressured-pass" ? e with { Outcome = "complete" } : e).ToArray();
        Assert.Empty(new SyntheticMatchService(fixture with { Events = events }).GetInsights(5400));
    }
    [Fact] public void UnrelatedRegainIsNotAttributedToPress()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = fixture.Away.Id, PlayerId = "ridge-11" } : e).ToArray();
        Assert.Empty(new SyntheticMatchService(fixture with { Events = events }).GetInsights(5400));
    }
    [Theory] [InlineData(-1)] [InlineData(5401)] public void InvalidClockIsRejected(int clock) => Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).GetState(clock));
    [Fact] public void ReplayUsesDurationHalfTimeAndPitchFromTheFixtureDefinition()
    {
        var source = SyntheticFixtureGenerator.Generate();
        const int duration = 3600;
        const int halfTime = 1800;
        const double pitchLength = 120;
        const double pitchWidth = 80;
        int ScaleClock(int seconds) => (int)Math.Round(seconds * (double)duration / source.DurationSeconds, MidpointRounding.AwayFromZero);
        var events = source.Events.Select(item =>
        {
            var newClock = ScaleClock(item.Event.ClockSeconds);
            return item with { Event = item.Event with
            {
                ClockSeconds = newClock,
                Period = item.Event.Type == PitchMind.Domain.Events.MatchEventType.HalfTime || newClock < halfTime ? 1 : 2,
                OccurredAt = item.Event.OccurredAt.AddSeconds(newClock - item.Event.ClockSeconds)
            } };
        }).ToArray();
        var frames = source.Frames.Select(frame => frame with
        {
            ClockSeconds = ScaleClock(frame.ClockSeconds),
            BallX = frame.BallX * pitchLength / source.PitchLengthMetres,
            BallY = frame.BallY * pitchWidth / source.PitchWidthMetres,
            Players = frame.Players.Select(player => player with
            {
                X = player.X * pitchLength / source.PitchLengthMetres,
                Y = player.Y * pitchWidth / source.PitchWidthMetres
            }).ToArray()
        }).ToArray();
        var fixture = source with
        {
            DurationSeconds = duration,
            HalfTimeSeconds = halfTime,
            PitchLengthMetres = pitchLength,
            PitchWidthMetres = pitchWidth,
            Events = events,
            Frames = frames
        };

        var service = new SyntheticMatchService(fixture);
        var fullTime = service.GetState(duration);
        Assert.Equal("full-time", fullTime.Status);
        Assert.Equal(2, service.GetState(halfTime).Period);
        Assert.Contains("x=0..120", fullTime.CoordinateConvention);
        Assert.Contains("y=0..80", fullTime.CoordinateConvention);
        Assert.Contains("1800 seconds", fullTime.CoordinateConvention);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetState(duration + 1));
        var outOfPitch = fixture.Frames.Select(frame => frame.ClockSeconds == duration
            ? frame with { Players = frame.Players.Select(player => player with { X = pitchLength + 1 }).ToArray() }
            : frame).ToArray();
        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = outOfPitch }));
    }
    [Fact] public void AudienceChangesCopyWithoutChangingEvidence()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()); var id = service.GetInsights(5400).Single().Id;
        var fan = service.Explain(id, 3309, "fan")!; var analyst = service.Explain(id, 3309, "analyst")!;
        Assert.Equal(fan.EventIds, analyst.EventIds); Assert.NotEqual(fan.Text, analyst.Text);
    }
    [Fact] public void DistantForwardDoesNotSupportPressureClaim()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select(f => f.ClockSeconds == 3293 ? f with
        { Players = f.Players.Select(p => p.PlayerId == "harbour-10" ? p with { X = 40 } : p).ToArray() } : f).ToArray();
        Assert.Empty(new SyntheticMatchService(fixture with { Frames = frames }).GetInsights(5400));
    }
    [Fact] public void IdenticalSelectedTimeInputAndPolicyProduceIdenticalDetection()
    {
        var service = new SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var first = service.GetInsights(5400).Single();
        var second = service.GetInsights(5400).Single();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.EventIds, second.EventIds);
        Assert.Equal(first.FrameIds, second.FrameIds);
        Assert.Equal(first.Conditions, second.Conditions);
        Assert.Equal(first.Summary, second.Summary);
    }
    [Fact] public void NamedPressCounterexamplesDoNotProducePositiveGolden()
    {
        var counterexamples = SyntheticFixtureGenerator.GeneratePressCounterexamples(seed: 42);

        Assert.Equal(new[]
        {
            SyntheticPressCounterexampleKind.MissingTracking,
            SyntheticPressCounterexampleKind.CompletedOpponentPass,
            SyntheticPressCounterexampleKind.WrongTeamRegain,
            SyntheticPressCounterexampleKind.DistantPressArrival
        }, counterexamples.Select(example => example.Kind));
        Assert.Equal(counterexamples.Count, counterexamples.Select(example => example.Name).Distinct().Count());
        Assert.All(counterexamples, example =>
            Assert.Empty(PressSequenceDetector.Detect(example.Fixture, 5400)));
    }
    [Fact] public void NonFiniteCoordinatesAreRejected()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select(f => f with { Players = f.Players.Select(p => p with { X = double.NaN }).ToArray() }).ToArray();
        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));
    }
    [Fact] public void DuplicateTrackingPlayerIdsAreRejectedAtFixtureBoundary()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select(frame => frame.ClockSeconds == 3293
            ? frame with { Players = frame.Players.Append(frame.Players[0]).ToArray() }
            : frame).ToArray();

        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));
    }
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(121, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(0, 81)]
    public void InvalidBallCoordinatesAreRejected(double ballX, double ballY)
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select((frame, index) => index == 0 ? frame with { BallX = ballX, BallY = ballY } : frame).ToArray();
        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));
    }
    [Theory]
    [InlineData(89, 68)]
    [InlineData(105, 44)]
    [InlineData(121, 68)]
    [InlineData(105, 91)]
    public void UnsupportedFootballPitchDimensionsAreRejected(double pitchLength, double pitchWidth)
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with
        {
            PitchLengthMetres = pitchLength,
            PitchWidthMetres = pitchWidth
        }));
    }
    [Fact] public void ExplanationsUseEvidenceTeamNames()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var service = new SyntheticMatchService(fixture with { Home = fixture.Home with { Name = "Estuary United" } });
        var insight = service.GetInsights(3309).Single();
        Assert.Contains("Estuary United", service.Explain(insight.Id, 3309)!.Text);
        Assert.DoesNotContain("Harbour", service.Explain(insight.Id, 3309)!.Text);
    }

    private static SyntheticFixture GenerateReplayGoldenFixture()
    {
        var source = SyntheticFixtureGenerator.Generate();
        var scenario = SyntheticFixtureGenerator.GenerateScenario(seed: source.Seed);
        var sourceById = source.Events.ToDictionary(item => item.Event.EventId);
        var events = scenario.Fixture.Events.Select(item => sourceById.TryGetValue(item.Event.EventId, out var original)
            ? original with { Event = original.Event with { Sequence = item.Event.Sequence } }
            : item).ToArray();
        return scenario.Fixture with { Events = Array.AsReadOnly(events) };
    }

    private sealed record ReplayGoldenDocument(string FixtureVersion, int FixtureSeed, string FixtureContentSha256,
        string Approval, IReadOnlyList<ReplayGoldenSnapshot> Snapshots);
    private sealed record ReplayGoldenSnapshot(int SelectedElapsedSeconds, string ReplayOutputSha256, long LastSequence,
        int Period, string Phase, int HomeScore, int AwayScore, int YellowCards, int HomeActivePlayers,
        int AwayActivePlayers, int InsightCount);

}
