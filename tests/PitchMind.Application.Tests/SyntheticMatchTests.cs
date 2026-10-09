using PitchMind.Application.Matches;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Events;
using PitchMind.Domain.Simulation;
using Xunit;
namespace PitchMind.Application.Tests;
public sealed class SyntheticMatchTests
{
    [Fact] public void ReplayExcludesFutureGoalsAndEvidence()
    {
        var service = new SyntheticMatchService();
        Assert.Equal(0, service.GetState(3308).HomeScore); Assert.Empty(service.GetInsights(3308));
        Assert.Equal(1, service.GetState(3309).HomeScore); Assert.Single(service.GetInsights(3309));
        Assert.Equal(0, service.GetState(4679).AwayScore); Assert.Equal(1, service.GetState(4680).AwayScore);
        Assert.Equal("harbour", service.GetState(3308).PossessionTeamId);
        Assert.Null(service.GetState(3309).PossessionTeamId);
        Assert.Equal("harbour", service.GetState(4680).PossessionTeamId);
        var insight = service.GetInsights(5400).Single();
        Assert.Equal("simulated", insight.Classification);
        Assert.Equal("press-sequence-v2", insight.DetectorVersion);
        Assert.Equal("unapproved-demo", insight.PolicyStatus);
        Assert.False(insight.Publishable);
        Assert.Null(insight.CanonicalRevision);
        Assert.Equal(5400, insight.SelectedTimeSeconds);
        Assert.Equal(190, insight.ThroughSequence);
        Assert.Equal("synthetic-v2", insight.SourceVersion);
        Assert.Contains(insight.Conditions, condition => condition.Contains("policy: press-sequence-v2", StringComparison.Ordinal));
        var graph = insight.EvidenceGraph!;
        Assert.Equal(insight.MatchId, graph.MatchId);
        Assert.Equal(insight.SourceVersion, graph.SourceVersion);
        Assert.Null(graph.CanonicalRevision);
        Assert.False(graph.Publishable);
        Assert.Equal("synthetic", graph.Provenance);
        Assert.Equal("pattern", graph.Nodes.Single(node => node.Id == graph.ClaimNodeId).TruthClass);
        Assert.All(insight.EventIds, eventId => Assert.Contains(graph.Nodes, node => node.EventId == eventId));
        Assert.Equal(insight.FrameIds.Order(), graph.Nodes.Where(node => node.FrameId is not null).Select(node => node.FrameId!).Order());
        var contextNode = Assert.Single(graph.Nodes, node => node.Kind == "metric");
        Assert.Equal("derivation", contextNode.TruthClass);
        Assert.Equal(0m, contextNode.NumericValue);
        Assert.Equal("active-players", contextNode.Unit);
        Assert.Contains(graph.Edges, edge => edge.FromNodeId == graph.ClaimNodeId && edge.ToNodeId == contextNode.Id && edge.Relation == "derived_from");
        Assert.All(graph.Edges, edge => Assert.Contains(edge.FromNodeId, graph.Nodes.Select(node => node.Id)));
        Assert.All(graph.Edges, edge => Assert.Contains(edge.ToNodeId, graph.Nodes.Select(node => node.Id)));
        Assert.Equal(graph.Nodes.Count(node => node.Kind == "event") - 1, graph.Edges.Count(edge => edge.Relation == "precedes"));
        Assert.Equal(insight.EventIds.Count + insight.FrameIds.Count + 1 + graph.Edges.Count(edge => edge.FromNodeId == contextNode.Id),
            graph.Edges.Count(edge => edge.Relation == "derived_from"));
        Assert.Null(service.GetEvidence(insight.Id, 3308));
        Assert.All(service.GetEvidence(insight.Id, 3309)!.Events, e => Assert.True(e.Event.ClockSeconds <= 3309));
    }
    [Fact] public void SelectedTimeProjectsCanonicalCardsRosterPossessionAndPhase()
    {
        var service = new SyntheticMatchService();
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
        var service = new SyntheticMatchService(fixture with { Frames = Array.Empty<TrackingFrame>() });
        Assert.Empty(service.GetInsights(5400));
        Assert.Throws<InvalidOperationException>(() => service.GetState(5400));
    }

    [Fact] public void KeyMomentsPrioritizeRecordedGoalsAndAvoidPassSpamWithoutLookingAhead()
    {
        var service = new SyntheticMatchService();
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
        var service = new SyntheticMatchService();
        var first = service.GetKeyMoments(5400);
        var second = service.GetKeyMoments(5400);
        Assert.Equal(first.Select(moment => moment.Id), second.Select(moment => moment.Id));

        var tactical = Assert.Single(first, moment => moment.Kind == "tactical-pattern");
        Assert.Equal("pattern", tactical.TruthClass);
        Assert.NotEmpty(tactical.EventIds);
        Assert.NotEmpty(tactical.FrameIds);
        Assert.NotEmpty(tactical.EvidenceClaimNodeIds);
        Assert.Equal("key-moment-v1-demo", tactical.PolicyVersion);
        Assert.False(tactical.Publishable);
    }

    [Fact] public void KeyMomentDecayAndThresholdCanSuppressStaleItems()
    {
        var service = new SyntheticMatchService();
        var policy = KeyMomentPolicy.UnapprovedDemoV1 with { RecencyHalfLifeSeconds = 1, MinimumRelevance = 99.9m };
        var moments = service.GetKeyMoments(5400, policy);
        Assert.DoesNotContain(moments, moment => moment.Kind == "goal");
    }

    [Fact] public void RepeatedShotsWithinTheDeduplicationWindowShareOneMomentAndEvidencePath()
    {
        var service = new SyntheticMatchService();
        var fixture = service.Fixture;
        var last = fixture.Events[^1].Event;
        var firstDuplicateId = Guid.NewGuid();
        var secondDuplicateId = Guid.NewGuid();
        var first = new SimulationEvent(MatchEvent.Create(firstDuplicateId, last.MatchId, last.Sequence + 1,
            MatchEventType.Shot, 5400, 2, last.OccurredAt.AddSeconds(1)), fixture.Home.Id, "", "shot", "saved");
        var second = new SimulationEvent(MatchEvent.Create(secondDuplicateId, last.MatchId, last.Sequence + 2,
            MatchEventType.Shot, 5400, 2, last.OccurredAt.AddSeconds(2)), fixture.Home.Id, "", "shot", "blocked");
        var events = fixture.Events.Concat([first, second]).ToArray();

        var moments = KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version, fixture.Provenance, null, 5400,
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
        var moments = KeyMomentGenerator.Generate(matchId.ToString(), "fixture-v1", "synthetic", null, 2701, 1,
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
        var moments = KeyMomentGenerator.Generate(matchId.ToString(), "fixture-v1", "synthetic", null, 191, 2,
            "home", "Home", "away", "Away", events, Array.Empty<TacticalInsight>());

        Assert.Equal(2, moments.Count(moment => moment.Kind == "shot"));
        Assert.Contains(moments, moment => moment.EventIds.Count == 2 && moment.EventIds.Contains(events[0].Event.EventId) && moment.EventIds.Contains(events[1].Event.EventId));
        Assert.Contains(moments, moment => moment.EventIds.SequenceEqual([events[2].Event.EventId]));
    }

    [Fact] public void KeyMomentGeneratorRejectsAnIncorrectSequenceWatermark()
    {
        var service = new SyntheticMatchService();
        var fixture = service.Fixture;
        Assert.Throws<ArgumentException>(() => KeyMomentGenerator.Generate(fixture.MatchId, fixture.Version,
            fixture.Provenance, null, 5400, fixture.Events[^1].Event.Sequence - 1, fixture.Home.Id, fixture.Home.Name,
            fixture.Away.Id, fixture.Away.Name, fixture.Events, Array.Empty<TacticalInsight>()));
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
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = "ridge", PlayerId = "ridge-11" } : e).ToArray();
        Assert.Empty(new SyntheticMatchService(fixture with { Events = events }).GetInsights(5400));
    }
    [Theory] [InlineData(-1)] [InlineData(5401)] public void InvalidClockIsRejected(int clock) => Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticMatchService().GetState(clock));
    [Fact] public void AudienceChangesCopyWithoutChangingEvidence()
    {
        var service = new SyntheticMatchService(); var id = service.GetInsights(5400).Single().Id;
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
        var service = new SyntheticMatchService();
        var first = service.GetInsights(5400).Single();
        var second = service.GetInsights(5400).Single();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.EventIds, second.EventIds);
        Assert.Equal(first.FrameIds, second.FrameIds);
        Assert.Equal(first.Conditions, second.Conditions);
        Assert.Equal(first.Summary, second.Summary);
    }
    [Fact] public void SameObservedSequenceReceivesNeutralDifferentScorelineContext()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var service = new SyntheticMatchService(fixture);
        var baseline = service.GetInsights(3309).Single();
        var later = service.GetInsights(4680).Single();
        var contextChangedEvents = fixture.Events.Select(item => item.Action == "yellow-card" ? item with
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
        Assert.Equal("Harbour FC 1–0 Ridge Athletic", baseline.Context!.Scoreline);
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
    [Fact] public void NonFiniteCoordinatesAreRejected()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var frames = fixture.Frames.Select(f => f with { Players = f.Players.Select(p => p with { X = double.NaN }).ToArray() }).ToArray();
        Assert.Throws<ArgumentException>(() => new SyntheticMatchService(fixture with { Frames = frames }));
    }
    [Fact] public void ExplanationsUseEvidenceTeamNames()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var service = new SyntheticMatchService(fixture with { Home = fixture.Home with { Name = "Estuary United" } });
        var insight = service.GetInsights(3309).Single();
        Assert.Contains("Estuary United", service.Explain(insight.Id, 3309)!.Text);
        Assert.DoesNotContain("Harbour", service.Explain(insight.Id, 3309)!.Text);
    }
}
