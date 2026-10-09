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
    [Fact] public void SuccessfulCirculationDoesNotBecomePressingTrap()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "pressured-pass" ? e with { Outcome = "complete" } : e).ToArray();
        Assert.Empty(new SyntheticMatchService(fixture with { Events = events }).GetInsights(5400));
    }
    [Fact] public void UnrelatedRegainIsNotAttributedToPress()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = "ridge" } : e).ToArray();
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
