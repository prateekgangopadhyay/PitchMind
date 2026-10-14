using System.Text.Json;
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
                SyntheticMatchService.ReplayAlgorithmVersion, PressingSequenceDetector.Version, golden.FixtureContentSha256);
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
            Version = "synthetic-v2-correction-1",
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
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Frames = Array.Empty<TrackingFrame>() }, 5400));
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
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Events = events }, 5400));
    }
    [Fact] public void UnrelatedRegainIsNotAttributedToPress()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Select(e => e.Action == "high-regain" ? e with { TeamId = "ridge" } : e).ToArray();
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Events = events }, 5400));
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
        Assert.Empty(PressingSequenceDetector.Detect(fixture with { Frames = frames }, 5400));
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
