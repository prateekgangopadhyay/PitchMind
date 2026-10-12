using System.Security.Cryptography;
using System.Text;
using System.Globalization;
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

    [Fact] public void SeedAndVersionProduceRepeatableSeedScopedMatchIdentity()
    {
        var first = SyntheticFixtureGenerator.Generate(seed: 42);
        var repeated = SyntheticFixtureGenerator.Generate(seed: 42);
        var differentSeed = SyntheticFixtureGenerator.Generate(seed: 43);

        Assert.Equal("synthetic-v3", first.Version);
        Assert.Equal(42, first.Seed);
        Assert.Equal(SyntheticFixtureGenerator.FixtureId.ToString(), first.MatchId);
        Assert.Equal(first.MatchId, repeated.MatchId);
        Assert.NotEqual(first.MatchId, differentSeed.MatchId);
        Assert.Equal(first.Events, repeated.Events);
        Assert.NotEqual(first.Events, differentSeed.Events);
        Assert.Empty(first.Events.Select(item => item.Event.EventId)
            .Intersect(differentSeed.Events.Select(item => item.Event.EventId)));
        Assert.All(first.Events, item =>
        {
            var expectedId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                $"{SyntheticFixtureGenerator.Version}:{first.Seed}:{item.Event.ClockSeconds}:{item.Event.Sequence}:{item.Action}")))
                .AsSpan(0, 16));
            Assert.Equal(expectedId, item.Event.EventId);
        });
        Assert.Equal(first.Frames.Count, repeated.Frames.Count);
        for (var index = 0; index < first.Frames.Count; index++)
        {
            Assert.Equal(first.Frames[index].Id, repeated.Frames[index].Id);
            Assert.Equal(first.Frames[index].ClockSeconds, repeated.Frames[index].ClockSeconds);
            Assert.Equal(first.Frames[index].BallX, repeated.Frames[index].BallX);
            Assert.Equal(first.Frames[index].BallY, repeated.Frames[index].BallY);
            Assert.Equal(first.Frames[index].Players, repeated.Frames[index].Players);
        }
        Assert.All(first.Events, item => Assert.Equal(first.MatchId, item.Event.MatchId.ToString()));
        Assert.All(differentSeed.Events, item => Assert.Equal(differentSeed.MatchId, item.Event.MatchId.ToString()));

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var cultureVariant = SyntheticFixtureGenerator.Generate(seed: 42);
            Assert.Equal(first.MatchId, cultureVariant.MatchId);
            Assert.Equal(first.Events, cultureVariant.Events);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
