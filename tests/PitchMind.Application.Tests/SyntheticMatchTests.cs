using PitchMind.Application.Matches;
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
    [Fact] public void UnrelatedRegainIsNotAttributedToPress()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = "ridge" } : e).ToArray();
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Events = events }, 5400));
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
