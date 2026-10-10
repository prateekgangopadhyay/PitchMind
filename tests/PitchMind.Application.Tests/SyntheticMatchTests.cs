using PitchMind.Application.Matches;
using PitchMind.Domain.Analytics;
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
        Assert.Equal(service.Fixture.SourceSha256, insight.SourceSha256);
        Assert.Equal(insight.SourceSha256, graph.SourceSha256);
        Assert.All(graph.Nodes, node => Assert.Equal(graph.SourceSha256, node.SourceSha256));
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

    [Theory]
    [InlineData("interpretation")]
    [InlineData("hypothesis")]
    public void EvidenceGraphPreservesQualifiedClaimTruthClasses(string truthClass)
    {
        var service = new SyntheticMatchService();
        var insight = service.GetInsights(5400).Single() with { ClaimTruthClass = truthClass };

        var graph = TacticalEvidenceGraphBuilder.Build(insight, service.Fixture.Events, service.Fixture.Frames);

        Assert.Equal(truthClass, graph.Nodes.Single(node => node.Id == graph.ClaimNodeId).TruthClass);
        Assert.All(graph.Nodes, node => Assert.Equal(service.Fixture.SourceSha256, node.SourceSha256));
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
}
