using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        Assert.Equal("synthetic", first.Provenance); Assert.Equal("synthetic-v1", first.Version);
        Assert.Equal(first.Events, second.Events); Assert.Equal(5400, first.DurationSeconds);
        Assert.Equal(MatchEventType.KickOff, first.Events[0].Event.Type);
        Assert.Equal(MatchEventType.FullTime, first.Events[^1].Event.Type);
        Assert.Equal(Enumerable.Range(0, first.Events.Count).Select(i => (long)i), first.Events.Select(e => e.Event.Sequence));
        Assert.Equal(first.Events.Count, first.Events.Select(e => e.Event.EventId).Distinct().Count());
        Assert.All(first.Events, item =>
        {
            Assert.Equal(first.MatchId, item.Event.MatchId.ToString());
            Assert.InRange(item.Event.ClockSeconds, 0, first.DurationSeconds);
            Assert.Equal(item.Event.ClockSeconds < 2700 ? 1 : 2, item.Event.Period);
        });
        Assert.All(first.Frames, frame =>
        {
            Assert.InRange(frame.ClockSeconds, 0, first.DurationSeconds);
            Assert.Equal(22, frame.Players.Count);
            Assert.Equal(22, frame.Players.Select(player => player.PlayerId).Distinct().Count());
            Assert.All(frame.Players, player =>
            {
                Assert.InRange(player.X, 0, 105);
                Assert.InRange(player.Y, 0, 68);
            });
        });
    }

    [Fact] public void ArtifactHashIsStableForTheSameSeedAndChangesWithTheSeed()
    {
        var first = SyntheticFixtureArtifact.Create(SyntheticFixtureGenerator.Generate(seed: 42));
        var repeat = SyntheticFixtureArtifact.Create(SyntheticFixtureGenerator.Generate(seed: 42));
        var otherSeed = SyntheticFixtureArtifact.Create(SyntheticFixtureGenerator.Generate(seed: 43));

        Assert.Equal(SyntheticFixtureArtifact.CurrentFormatVersion, first.FormatVersion);
        Assert.Equal(first.Json, repeat.Json);
        Assert.Equal(first.ContentSha256, repeat.ContentSha256);
        Assert.NotEqual(first.ContentSha256, otherSeed.ContentSha256);
        Assert.NotEqual(first.Fixture.MatchId, otherSeed.Fixture.MatchId);
        using var payload = JsonDocument.Parse(first.Json);
        Assert.Equal(SyntheticFixtureArtifact.CurrentFormatVersion, payload.RootElement.GetProperty("formatVersion").GetString());
        Assert.Equal(first.Fixture.MatchId, payload.RootElement.GetProperty("fixture").GetProperty("matchId").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.Json))).ToLowerInvariant(), first.ContentSha256);
        Assert.Equal("a10b2e7e1e7a57e82d94acd5d407a9535f1fbc62ac0938e96522059dda0f3cf4", first.ContentSha256);
    }

    [Fact] public void PressingGoldenRetainsOrderedSamePeriodEvidence()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var actions = fixture.Events
            .Where(item => item.Event.ClockSeconds is >= 3290 and <= 3309)
            .ToArray();

        Assert.Equal(new[] { 3290, 3293, 3296, 3299, 3304, 3308, 3309 }, actions.Select(item => item.Event.ClockSeconds));
        Assert.All(actions, item => Assert.Equal(2, item.Event.Period));
        Assert.Equal(actions.OrderBy(item => item.Event.Sequence).Select(item => item.Event.Sequence), actions.Select(item => item.Event.Sequence));
        Assert.Equal(MatchEventType.Goal, actions[^1].Event.Type);
    }

    [Fact] public void PressingGoldenCapturesSpatialTriggerRegainAndSeparatedCounterexample()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        static PitchPosition Player(TrackingFrame frame, string id) => frame.Players.Single(player => player.PlayerId == id);
        static double Distance(PitchPosition left, PitchPosition right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        var pressFrame = fixture.Frames.Single(frame => frame.ClockSeconds == 3293);
        var pressEvent = fixture.Events.Single(item => item.Event.ClockSeconds == pressFrame.ClockSeconds);
        var keeper = Player(pressFrame, "ridge-1");
        var pressingForward = Player(pressFrame, "harbour-10");
        Assert.Equal(MatchEventType.Pressure, pressEvent.Event.Type);
        Assert.Equal(pressingForward.PlayerId, pressEvent.PlayerId);
        Assert.InRange(Distance(keeper, pressingForward), 0, 3);
        Assert.InRange(Math.Sqrt(Math.Pow(keeper.X - pressFrame.BallX, 2) + Math.Pow(keeper.Y - pressFrame.BallY, 2)), 0, 1);

        var regainFrame = fixture.Frames.Single(frame => frame.ClockSeconds == 3299);
        var regainEvent = fixture.Events.Single(item => item.Event.ClockSeconds == regainFrame.ClockSeconds);
        var ballWinner = Player(regainFrame, "harbour-11");
        var pressedDefender = Player(regainFrame, "ridge-2");
        Assert.Equal(MatchEventType.Turnover, regainEvent.Event.Type);
        Assert.Equal(ballWinner.PlayerId, regainEvent.PlayerId);
        Assert.InRange(Distance(ballWinner, pressedDefender), 0, 2);
        Assert.InRange(Math.Sqrt(Math.Pow(ballWinner.X - regainFrame.BallX, 2) + Math.Pow(ballWinner.Y - regainFrame.BallY, 2)), 0, 1);

        var separatedFrame = fixture.Frames.Single(frame => frame.ClockSeconds == 3000);
        var separatedForward = Player(separatedFrame, "harbour-10");
        Assert.True(Math.Sqrt(Math.Pow(separatedForward.X - separatedFrame.BallX, 2) + Math.Pow(separatedForward.Y - separatedFrame.BallY, 2)) > 10);
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
            Assert.All(localized.Frames.SelectMany(frame => frame.Players), player => Assert.InRange(player.Y, 0, 68));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact] public void GovernedScenarioCoversFullMatchDisciplineSubstitutionsAndPossessionPhases()
    {
        var scenario = SyntheticFixtureGenerator.GenerateScenario(seed: 42);
        var fixture = scenario.Fixture;

        Assert.Equal("synthetic", fixture.Provenance);
        Assert.Equal("synthetic-v3", fixture.Version);
        Assert.Equal("c9f91d42ec85e50887a3855be6ff749f030b234a58768de6fd492553e9d4067b", scenario.ContentSha256);
        Assert.Equal(5400, fixture.DurationSeconds);
        Assert.Contains(fixture.Events, e => e.Event.ClockSeconds == 0 && e.Event.Period == 1 && e.Event.Type == MatchEventType.KickOff);
        Assert.Contains(fixture.Events, e => e.Event.ClockSeconds == 5400 && e.Event.Period == 2 && e.Event.Type == MatchEventType.FullTime);
        Assert.Contains(fixture.Events, e => e.Event.Period == 1 && e.Event.ClockSeconds < 2700);
        Assert.Contains(fixture.Events, e => e.Event.Period == 2 && e.Event.ClockSeconds >= 2700);
        Assert.Equal(Enumerable.Range(0, fixture.Events.Count).Select(i => (long)i), fixture.Events.Select(e => e.Event.Sequence));
        Assert.Equal(fixture.Events.Count, fixture.Events.Select(e => e.Event.EventId).Distinct().Count());
        Assert.Equal(fixture.Events.Count + 4, scenario.Events.Count);
        Assert.Equal(Enumerable.Range(0, scenario.Events.Count).Select(i => (long)i), scenario.Events.Select(e => e.Sequence));
        Assert.All(scenario.Events, entry =>
        {
            Assert.InRange(entry.ClockSeconds, 0, 5400);
            Assert.Equal(entry.ClockSeconds < 2700 ? 1 : 2, entry.Period);
            if (entry.PlayerId.Length > 0)
                Assert.Equal(entry.TeamId, scenario.Squad.Single(player => player.Id == entry.PlayerId).TeamId);
        });

        Assert.Contains(scenario.Events, e => e.Type == SyntheticScenarioEventKind.YellowCard && e.Period == 1);
        Assert.Contains(scenario.Events, e => e.Type == SyntheticScenarioEventKind.RedCard && e.Period == 2);
        Assert.Equal(2, scenario.Events.Count(e => e.Type == SyntheticScenarioEventKind.Substitution));
        Assert.Equal(Enumerable.Range(0, scenario.Events.Count).Select(i => (long)i), scenario.Events.Select(e => e.Sequence));
        Assert.Equal(scenario.Events.Count, scenario.Events.Select(e => e.EventId).Distinct().Count());
        Assert.All(fixture.Events, source => Assert.Contains(scenario.Events,
            e => e.EventId == source.Event.EventId && e.SourceSequence == source.Event.Sequence));
        Assert.All(scenario.Events, action => Assert.Contains(scenario.Squad, player => player.Id == action.PlayerId || action.PlayerId == ""));
        Assert.All(scenario.Events.Where(e => e.Type == SyntheticScenarioEventKind.Substitution), action =>
        {
            Assert.Contains(scenario.Squad, player => player.Id == action.RelatedPlayerId);
            Assert.Equal(action.TeamId, scenario.Squad.Single(player => player.Id == action.RelatedPlayerId).TeamId);
        });

        Assert.Equal(0, scenario.PossessionPhases[0].StartClockSeconds);
        Assert.Equal(5400, scenario.PossessionPhases[^1].EndClockSecondsExclusive);
        Assert.All(scenario.PossessionPhases, phase => Assert.True(phase.EndClockSecondsExclusive > phase.StartClockSeconds));
        Assert.All(scenario.PossessionPhases, phase => Assert.Equal(phase.StartClockSeconds < 2700 ? 1 : 2, phase.Period));
        Assert.All(scenario.PossessionPhases.Zip(scenario.PossessionPhases.Skip(1)), pair =>
            Assert.Equal(pair.First.EndClockSecondsExclusive, pair.Second.StartClockSeconds));
        Assert.Contains(scenario.PossessionPhases, phase => phase.Phase == "high-regain-transition" && phase.TeamId == fixture.Home.Id);
        Assert.Contains(scenario.PossessionPhases, phase => phase.Period == 1 && phase.TeamId == fixture.Home.Id);
        Assert.Contains(scenario.PossessionPhases, phase => phase.Period == 2 && phase.TeamId == fixture.Away.Id);
        Assert.Equal(fixture.Away.Id, scenario.PossessionPhases.Single(phase => phase.StartClockSeconds <= 3293 && phase.EndClockSecondsExclusive > 3293).TeamId);
        Assert.Equal(fixture.Home.Id, scenario.PossessionPhases.Single(phase => phase.StartClockSeconds <= 3299 && phase.EndClockSecondsExclusive > 3299).TeamId);
        Assert.Equal(fixture.Home.Id, scenario.PossessionPhases.Single(phase => phase.StartClockSeconds <= 3309 && phase.EndClockSecondsExclusive > 3309).TeamId);

        var beforeHomeChange = scenario.Frames.Single(frame => frame.ClockSeconds == 3570);
        var afterHomeChange = scenario.Frames.Single(frame => frame.ClockSeconds == 3600);
        Assert.Contains(beforeHomeChange.Players, player => player.PlayerId == "harbour-9");
        Assert.DoesNotContain(beforeHomeChange.Players, player => player.PlayerId == "harbour-12");
        Assert.DoesNotContain(afterHomeChange.Players, player => player.PlayerId == "harbour-9");
        Assert.Contains(afterHomeChange.Players, player => player.PlayerId == "harbour-12");

        var beforeDismissal = scenario.Frames.Single(frame => frame.ClockSeconds == 4170);
        var afterDismissal = scenario.Frames.Single(frame => frame.ClockSeconds == 4200);
        Assert.Equal(22, beforeDismissal.Players.Count);
        Assert.Equal(21, afterDismissal.Players.Count);
        Assert.DoesNotContain(afterDismissal.Players, player => player.PlayerId == "ridge-6");
        Assert.All(scenario.Frames, frame =>
        {
            Assert.Equal(frame.ClockSeconds >= 4200 ? 21 : 22, frame.Players.Count);
            Assert.Equal(frame.Players.Count, frame.Players.Select(player => player.PlayerId).Distinct().Count());
            Assert.All(frame.Players, player =>
            {
                Assert.InRange(player.X, 0, 105);
                Assert.InRange(player.Y, 0, 68);
            });
        });
    }

    [Fact] public void GovernedScenarioAnnotationsAreDeterministicAcrossSeedsAndRegeneration()
    {
        var first = SyntheticFixtureGenerator.GenerateScenario(seed: -19);
        var repeated = SyntheticFixtureGenerator.GenerateScenario(seed: -19);
        var other = SyntheticFixtureGenerator.GenerateScenario(seed: -20);

        Assert.Equal(first.Fixture.MatchId, repeated.Fixture.MatchId);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.Equal(first.ContentSha256, repeated.ContentSha256);
        Assert.Equal(first.Events, repeated.Events);
        Assert.Equal(first.PossessionPhases, repeated.PossessionPhases);
        Assert.Equal(first.Frames.Select(frame => (frame.Id, frame.ClockSeconds, frame.BallX, frame.BallY,
                Players: string.Join(",", frame.Players.Select(player => player.PlayerId)))),
            repeated.Frames.Select(frame => (frame.Id, frame.ClockSeconds, frame.BallX, frame.BallY,
                Players: string.Join(",", frame.Players.Select(player => player.PlayerId)))));
        Assert.NotEqual(first.Fixture.MatchId, other.Fixture.MatchId);
        Assert.NotEqual(first.ContentSha256, other.ContentSha256);
        Assert.NotEqual(first.Events.Single(e => e.Type == SyntheticScenarioEventKind.YellowCard).EventId,
            other.Events.Single(e => e.Type == SyntheticScenarioEventKind.YellowCard).EventId);
    }

    [Theory]
    [InlineData(41)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(-19)]
    public void PossessionAnnotationsAgreeWithSeededCirculationEvents(int seed)
    {
        var scenario = SyntheticFixtureGenerator.GenerateScenario(seed);

        foreach (var source in scenario.Fixture.Events.Where(entry => entry.Action == "circulation"))
        {
            var phase = scenario.PossessionPhases.Single(candidate =>
                candidate.StartClockSeconds <= source.Event.ClockSeconds &&
                candidate.EndClockSecondsExclusive > source.Event.ClockSeconds);
            Assert.Equal(source.TeamId, phase.TeamId);
        }
    }
}
