using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using PitchMind.Domain.Events;
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
}
