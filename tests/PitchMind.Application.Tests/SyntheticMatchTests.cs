using PitchMind.Application.Matches;
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
        var insight = service.GetInsights(5400).Single(); Assert.Null(service.GetEvidence(insight.Id, 3308));
        Assert.All(service.GetEvidence(insight.Id, 3309)!.Events, e => Assert.True(e.Event.ClockSeconds <= 3309));
    }
    [Fact] public void MissingTrackingSuppressesSpatialClaim()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Frames = Array.Empty<TrackingFrame>() }, 5400));
    }
    [Fact] public void SuccessfulCirculationDoesNotBecomePressingTrap()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "pressured-pass" ? e with { Outcome = "complete" } : e).ToArray();
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Events = events }, 5400));
    }
    [Fact] public void DemonstrationPolicyIsVersionedUnreviewedAndDrivesEvidenceText()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var policy = PressingSequenceDetector.DemonstrationPolicy;
        var service = new SyntheticMatchService(fixture);
        var insight = service.GetInsights(5400).Single();
        var evidence = service.GetEvidence(insight.Id, 5400)!;

        Assert.Equal("press-sequence-v1", policy.Version);
        Assert.Equal("demonstration-pending-review", policy.ReviewStatus);
        Assert.Equal(policy.Version, insight.DetectorVersion);
        Assert.Contains(evidence.Conditions, condition => condition.Contains("within 3 metres", StringComparison.Ordinal));
        Assert.Contains(policy.Limitations, limitation => limitation.Contains("unreviewed synthetic demonstration", StringComparison.Ordinal));
    }
    [Fact] public void DetectorUsesPolicyDistanceInsteadOfAnEmbeddedThreshold()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var stricterPolicy = PressingSequenceDetector.DemonstrationPolicy with { MaximumPressureDistanceMetres = 0.1 };

        Assert.Empty(PressingSequenceDetector.Detect(fixture, 5400, stricterPolicy));
    }
    [Fact] public void DetectorRejectsInvalidPolicyOverrides()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var invalidPolicy = PressingSequenceDetector.DemonstrationPolicy with { RequiredActionSequence = Array.Empty<string>() };

        Assert.Throws<InvalidOperationException>(() => PressingSequenceDetector.Detect(fixture, 5400, invalidPolicy));
    }
    [Fact] public void UnrelatedRegainIsNotAttributedToPress()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = "ridge" } : e).ToArray();
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Events = events }, 5400));
    }
    [Theory] [InlineData(-1)] [InlineData(5401)] public void InvalidClockIsRejected(int clock) => Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).GetState(clock));
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
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Frames = frames }, 5400));
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
