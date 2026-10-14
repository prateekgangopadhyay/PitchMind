using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using PitchMind.Domain.Events;
using System.Globalization;
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

    [Fact] public void MatchIdentityIsStableForASeedAndDistinctAcrossSeeds()
    {
        var first = SyntheticFixtureGenerator.Generate(seed: 41);
        var repeated = SyntheticFixtureGenerator.Generate(seed: 41);
        var other = SyntheticFixtureGenerator.Generate(seed: 43);

        Assert.Equal(first.MatchId, repeated.MatchId);
        Assert.NotEqual(first.MatchId, other.MatchId);
        Assert.All(first.Events, item => Assert.Equal(first.MatchId, item.Event.MatchId.ToString()));
        Assert.All(other.Events, item => Assert.Equal(other.MatchId, item.Event.MatchId.ToString()));
        Assert.NotEqual(first.Events[0].Event.EventId, other.Events[0].Event.EventId);
    }

    [Fact] public void SeededIdentityDoesNotDependOnCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var localized = SyntheticFixtureGenerator.Generate(seed: -41);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = SyntheticFixtureGenerator.Generate(seed: -41);

            Assert.Equal(invariant.MatchId, localized.MatchId);
            Assert.Equal(invariant.Events.Select(e => e.Event.EventId), localized.Events.Select(e => e.Event.EventId));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
