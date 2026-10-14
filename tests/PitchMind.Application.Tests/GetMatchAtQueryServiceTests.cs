using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Xunit;
using ReplaySnapshot = PitchMind.Application.Abstractions.MatchReplaySnapshot;

namespace PitchMind.Application.Tests;

public sealed class GetMatchAtQueryServiceTests
{
    [Fact]
    public async Task ComposesProjectionAndOnlyItsSelectedTimeEventWindow()
    {
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var aggregate = new MatchAggregate(matchId, "home", "away", players, players.Select(p => p.Item1), "synthetic");
        var kickoff = new SimulationEvent(Event(matchId, 0, 0) with { Type = MatchEventType.KickOff }, "home", "home-1", "kickoff", "started");
        var pass = new SimulationEvent(Event(matchId, 1, 3308), "home", "home-2", "pass", "complete");
        aggregate.Apply(kickoff);
        aggregate.Apply(pass);
        var state = aggregate.Snapshot();
        var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        var projection = new SelectedMatchProjection(state, 2, 2, 1, 3308, "aggregate-v1", new string('a', 64), stateHash);
        var events = new[] { kickoff.Event, pass.Event };
        var context = new MatchReplayContext("fixture-v1", "Home", "Away", 5400, 2700, 105, 68,
            "metres-v1", "metre coordinates, x=0..105, y=0..68; attacking direction changes at 2700 seconds", "synthetic");
        var frame = new MatchReplayFrame(new TrackingFrame("frame-0", 0, 52.5, 34, []), 0, 1,
            "metres-v1", projection.SourceSha256);
        var reader = new ProjectionReaderFake(projection, events, aggregate.ReplaySeed, [kickoff, pass], context, [frame]);
        var query = new GetMatchAtQuery(matchId, 3308, 3, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version);

        var result = await new GetMatchAtQueryService(reader).ExecuteAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(query.SelectedSeconds, result.SelectedSeconds);
        Assert.Equal(2, result.Projection.CanonicalRevision);
        Assert.Equal(2, result.Projection.ProjectionRevision);
        Assert.Equal(new long[] { 0, 1 }, result.Events.Select(item => item.Sequence));
        Assert.Equal(query, reader.LastQuery);
        Assert.Equal(stateHash, result.ReplaySha256);
        Assert.Empty(result.DerivedInsights);
        Assert.Equal(PressSequencePolicy.UnapprovedDemoV2.Version, result.DetectorVersion);
        Assert.Matches("^[0-9a-f]{64}$", result.ReplayInputSha256);
        Assert.Matches("^[0-9a-f]{64}$", result.DerivedOutputSha256);
        Assert.Equal(stateJson, JsonSerializer.Serialize(result.ReplayedState, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task ReplaysTheSameDerivedOutputAtTheSelectedTimeAndExcludesTheLaterGoal()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var matchId = new MatchId(Guid.Parse(fixture.MatchId));
        var replaySeed = new MatchAggregate(matchId, fixture.Home.Id, fixture.Away.Id,
            fixture.Home.Players.Concat(fixture.Away.Players).Select(player => (player.Id, player.TeamId)),
            fixture.Frames.Single(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId), fixture.Provenance,
            new MatchCompetitionRules("synthetic-test-competition", "rules-v1", 0)).ReplaySeed;
        var context = new MatchReplayContext(fixture.Version, fixture.Home.Name, fixture.Away.Name, fixture.DurationSeconds,
            fixture.HalfTimeSeconds, fixture.PitchLengthMetres, fixture.PitchWidthMetres, "metres-v1",
            fixture.CoordinateConvention, fixture.Provenance);
        var sourceHash = new string('a', 64);

        async Task<MatchAtResult> Replay(int selectedSeconds, bool addExtraTrackingSample = false)
        {
            var events = fixture.Events.Where(item => item.Event.ClockSeconds <= selectedSeconds).ToArray();
            var aggregate = MatchAggregate.FromReplaySeed(matchId, replaySeed);
            foreach (var item in events) aggregate.Apply(item);
            var state = aggregate.Snapshot();
            var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
            var projection = new SelectedMatchProjection(state, 1, 1, state.LastSequence, state.ClockSeconds,
                "aggregate-v1", sourceHash, stateHash);
            var selectedFrames = fixture.Frames.Where(frame => frame.ClockSeconds <= selectedSeconds).ToList();
            if (addExtraTrackingSample)
            {
                var nonArrival = selectedFrames.Single(frame => frame.ClockSeconds == 3290);
                selectedFrames.Add(nonArrival with { Id = "frame-extra-3290" });
            }
            var frames = selectedFrames.OrderBy(frame => frame.ClockSeconds).ThenBy(frame => frame.Id, StringComparer.Ordinal)
                .Select((frame, index) => new MatchReplayFrame(frame, index,
                    frame.ClockSeconds < fixture.HalfTimeSeconds ? 1 : 2, "metres-v1", sourceHash)).ToArray();
            var reader = new ProjectionReaderFake(projection, events.Select(item => item.Event).ToArray(), replaySeed,
                events, context, frames);
            return (await new GetMatchAtQueryService(reader).ExecuteAsync(new(matchId, selectedSeconds, 1,
                "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None))!;
        }

        var before = await Replay(3308);
        var atGoal = await Replay(3309);
        var repeated = await Replay(3309);
        var withExtraSameSecondSample = await Replay(3309, addExtraTrackingSample: true);

        Assert.Empty(before.DerivedInsights);
        Assert.NotEqual(before.DerivedOutputSha256, atGoal.DerivedOutputSha256);
        var insight = Assert.Single(atGoal.DerivedInsights);
        Assert.Equal(7, insight.EventIds.Count);
        Assert.Equal("simulated", insight.Classification);
        Assert.Equal("press-sequence-v2", insight.DetectorVersion);
        Assert.Equal("unapproved-demo", insight.PolicyStatus);
        Assert.False(insight.Publishable);
        Assert.Equal("tactical-context-v1", insight.Context!.SchemaVersion);
        Assert.Equal("Harbour FC 1–0 Ridge Athletic", insight.Context.Scoreline);
        Assert.Equal("home", insight.Context.TeamRole);
        Assert.Equal(0, insight.Context.NumericalAdvantage);
        Assert.Equal("SecondHalf", insight.Context.MatchPhase);
        Assert.Equal(1L, insight.CanonicalRevision);
        Assert.Equal(atGoal.SelectedSeconds, insight.SelectedTimeSeconds);
        Assert.Equal(atGoal.Projection.AsOfSequence, insight.ThroughSequence);
        Assert.Equal(fixture.Version, insight.SourceVersion);
        Assert.Equal(8, Assert.Single(withExtraSameSecondSample.DerivedInsights).FrameIds.Count);
        Assert.Equal(atGoal.DerivedOutputSha256, repeated.DerivedOutputSha256);
        Assert.Equal(atGoal.ReplayInputSha256, repeated.ReplayInputSha256);
        Assert.Equal(JsonSerializer.Serialize(atGoal.DerivedInsights), JsonSerializer.Serialize(repeated.DerivedInsights));
        Assert.Equal("synthetic-test-competition", atGoal.ReplayedState.CompetitionId);
        Assert.Equal("rules-v1", atGoal.ReplayedState.CompetitionRulesVersion);
        Assert.Equal(0, atGoal.ReplayedState.MaximumSubstituteReentriesPerPlayer);
        Assert.DoesNotContain(insight.EventIds, id => !atGoal.Events.Any(item => item.EventId == id));
        Assert.DoesNotContain(insight.FrameIds, id => fixture.Frames.Single(frame => frame.Id == id).ClockSeconds > atGoal.SelectedSeconds);
    }

    [Fact]
    public async Task MissingProjectionReturnsNoMatchResult()
    {
        var reader = new ProjectionReaderFake(null, []);
        var result = await new GetMatchAtQueryService(reader).ExecuteAsync(
            new(MatchId.New(), 10, 1, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(reader.EventsRead);
    }

    [Fact]
    public async Task RejectsProjectionFromAnUnexpectedAlgorithmVersion()
    {
        var matchId = MatchId.New();
        var reader = new ProjectionReaderFake(Projection(matchId, 1, 1, 0, 0) with
        {
            AlgorithmVersion = "aggregate-v2"
        }, [Event(matchId, 0, 0)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 0, 1, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
        Assert.Single(reader.EventsRead);
    }

    [Fact]
    public async Task RejectsFutureEventsReturnedByAReaderAdapter()
    {
        var matchId = MatchId.New();
        var reader = new ProjectionReaderFake(Projection(matchId, 2, 2, 1, 3308),
            [Event(matchId, 0, 0), Event(matchId, 1, 3309)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNonContiguousEventSequences()
    {
        var matchId = MatchId.New();
        var projection = Projection(matchId, 2, 2, 1, 3308);
        var reader = new ProjectionReaderFake(projection,
            [Event(matchId, 0, 3307), Event(matchId, 2, 3308)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsBackwardsEventClocks()
    {
        var matchId = MatchId.New();
        var projection = Projection(matchId, 2, 2, 1, 3308);
        var reader = new ProjectionReaderFake(projection,
            [Event(matchId, 0, 3308), Event(matchId, 1, 3307)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsAnUnregisteredDetectorVersionInsteadOfUsingTheLatest()
    {
        var matchId = MatchId.New();
        var reader = new ProjectionReaderFake(null, []);
        await Assert.ThrowsAsync<NotSupportedException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 0, 1, "aggregate-v1", "press-sequence-v99"), CancellationToken.None));
        Assert.Null(reader.LastQuery);
    }

    [Fact]
    public async Task RejectsMissingFutureAndUnmappedReplayFramesButAllowsSameSecondSamples()
    {
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 12).Select(i => ($"home-{i}", "home"))
            .Concat(Enumerable.Range(1, 11).Select(i => ($"away-{i}", "away"))).ToArray();
        var activePlayers = players.Select(player => player.Item1).Where(playerId => playerId != "home-12");
        var aggregate = new MatchAggregate(matchId, "home", "away", players, activePlayers, "synthetic");
        var kickoff = new SimulationEvent(Event(matchId, 0, 0) with { Type = MatchEventType.KickOff }, "home", "home-1", "kickoff", "started");
        aggregate.Apply(kickoff);
        var state = aggregate.Snapshot();
        var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        var projection = new SelectedMatchProjection(state, 1, 1, 0, 0, "aggregate-v1", new string('a', 64), stateHash);
        var context = new MatchReplayContext("fixture-v1", "Home", "Away", 5400, 2700, 105, 68,
            "metres-v1", "metre coordinates, x=0..105, y=0..68; attacking direction changes at 2700 seconds", "synthetic");
        var validFrame = new MatchReplayFrame(new TrackingFrame("frame-0", 0, 52.5, 34,
            [new PitchPosition("home-1", "home", 10, 10)]), 0, 1, "metres-v1", projection.SourceSha256);

        ProjectionReaderFake Reader(MatchReplayContext? replayContext, IReadOnlyList<MatchReplayFrame> frames,
            bool enforceFrameTimeFilter = true) => new(projection, [kickoff.Event], aggregate.ReplaySeed, [kickoff],
                replayContext, frames, enforceFrameTimeFilter);
        var query = new GetMatchAtQuery(matchId, 0, 1, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(Reader(context, [])).ExecuteAsync(query, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(Reader(context with { CoordinateVersion = "unitless-v1" }, [validFrame])).ExecuteAsync(query, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(Reader(context,
            [validFrame with { Frame = validFrame.Frame with { ClockSeconds = 1 } }], enforceFrameTimeFilter: false))
            .ExecuteAsync(query, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(Reader(context,
            [validFrame with { Frame = validFrame.Frame with { Players = [new PitchPosition("ghost", "home", 10, 10)] } }]))
            .ExecuteAsync(query, CancellationToken.None));

        var sameSecond = await new GetMatchAtQueryService(Reader(context,
            [validFrame, validFrame with { Sequence = 1, Frame = validFrame.Frame with { Id = "frame-0b" } }]))
            .ExecuteAsync(query, CancellationToken.None);
        Assert.Empty(sameSecond!.DerivedInsights);
    }

    [Fact]
    public async Task HalftimeTrackingFrameMustUseSecondPeriodConvention()
    {
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var aggregate = new MatchAggregate(matchId, "home", "away", players, players.Select(p => p.Item1), "synthetic");
        var kickoff = new SimulationEvent(Event(matchId, 0, 0) with { Type = MatchEventType.KickOff }, "home", "home-1", "kickoff", "started");
        aggregate.Apply(kickoff);
        var state = aggregate.Snapshot();
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var sourceHash = new string('a', 64);
        var projection = new SelectedMatchProjection(state, 1, 1, 0, 0, "aggregate-v1", sourceHash, stateHash);
        var context = new MatchReplayContext("fixture-v1", "Home", "Away", 5400, 2700, 105, 68,
            "metres-v1", "metre coordinates, x=0..105, y=0..68; attacking direction changes at 2700 seconds", "synthetic");
        var halftimeFrame = new MatchReplayFrame(new TrackingFrame("half", 2700, 52.5, 34, []), 0, 1,
            "metres-v1", sourceHash);
        var reader = new ProjectionReaderFake(projection, [kickoff.Event], aggregate.ReplaySeed, [kickoff], context, [halftimeFrame]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 2700, 1, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
    }

    [Fact]
    public async Task ReplayFrameCannotUsePlayerAfterSubstitutionAtAnEarlierClock()
    {
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 12).Select(i => ($"home-{i}", "home"))
            .Concat(Enumerable.Range(1, 11).Select(i => ($"away-{i}", "away"))).ToArray();
        var activePlayers = players.Select(player => player.Item1).Where(playerId => playerId != "home-12");
        var aggregate = new MatchAggregate(matchId, "home", "away", players, activePlayers, "synthetic");
        var kickoff = new SimulationEvent(Event(matchId, 0, 0) with { Type = MatchEventType.KickOff }, "home", "home-1", "kickoff", "started");
        var substitution = new SimulationEvent(Event(matchId, 1, 10) with { Type = MatchEventType.Substitution },
            "home", "home-1", "substitution", "complete", "home-12");
        aggregate.Apply(kickoff);
        aggregate.Apply(substitution);
        var state = aggregate.Snapshot();
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var sourceHash = new string('a', 64);
        var projection = new SelectedMatchProjection(state, 2, 2, 1, 10, "aggregate-v1", sourceHash, stateHash);
        var context = new MatchReplayContext("fixture-v1", "Home", "Away", 5400, 2700, 105, 68,
            "metres-v1", "metre coordinates, x=0..105, y=0..68; attacking direction changes at 2700 seconds", "synthetic");
        var frame = new MatchReplayFrame(new TrackingFrame("frame-after-sub", 10, 52.5, 34,
            [new PitchPosition("home-1", "home", 10, 10)]), 0, 1, "metres-v1", sourceHash);
        var reader = new ProjectionReaderFake(projection, [kickoff.Event, substitution.Event], aggregate.ReplaySeed,
            [kickoff, substitution], context, [frame]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 10, 2, "aggregate-v1", PressSequencePolicy.UnapprovedDemoV2.Version), CancellationToken.None));
    }

    private static SelectedMatchProjection Projection(MatchId matchId, long canonicalRevision,
        int projectionRevision, long sequence, int clock) => new(
        new MatchState(matchId.ToString(), clock, 1, MatchPhase.FirstHalf, 0, 0, null, 11, 11, [], sequence, "synthetic"),
        canonicalRevision, projectionRevision, sequence, clock, "aggregate-v1", new string('a', 64), new string('b', 64));

    private static MatchEvent Event(MatchId matchId, long sequence, int clock) =>
        MatchEvent.Create(Guid.NewGuid(), matchId, sequence, MatchEventType.Pass, clock, 1, DateTimeOffset.UnixEpoch.AddSeconds(clock));

    private sealed class ProjectionReaderFake(SelectedMatchProjection? projection,
        IReadOnlyList<MatchEvent> events, MatchReplaySeed? replaySeed = null,
        IReadOnlyList<SimulationEvent>? replayEvents = null, MatchReplayContext? replayContext = null,
        IReadOnlyList<MatchReplayFrame>? replayFrames = null, bool enforceFrameTimeFilter = true) : IMatchProjectionReader
    {
        public GetMatchAtQuery? LastQuery { get; private set; }
        public List<MatchEvent> EventsRead { get; } = [];

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PersistedMatchProjection>>([]);

        public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
            long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
            Task.FromResult(projection is not null && projection.CanonicalRevision <= (maximumCanonicalRevision ?? long.MaxValue) &&
                projection.ProjectionRevision <= (maximumProjectionRevision ?? int.MaxValue) ? projection : null);

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection selectedProjection,
            int selectedSeconds, long afterSequence, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new PersistedMatchEventPage(Array.Empty<MatchEvent>(), null, false));

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
        {
            LastQuery = new(matchId, selectedSeconds, upToCanonicalRevision, algorithmVersion, PressSequencePolicy.UnapprovedDemoV2.Version);
            return Task.FromResult(projection);
        }

        public async Task<ReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
        {
            var selected = await GetAtAsync(matchId, selectedSeconds, upToCanonicalRevision, algorithmVersion, cancellationToken);
            if (selected is null) return null;
            var selectedEvents = new List<MatchEvent>();
            await foreach (var item in ReadEventsAtAsync(selected, selectedSeconds, cancellationToken)) selectedEvents.Add(item);
            var selectedReplayEvents = new List<SimulationEvent>();
            await foreach (var item in ReadReplayEventsAtAsync(selected, selectedSeconds, cancellationToken)) selectedReplayEvents.Add(item);
            var selectedFrames = new List<MatchReplayFrame>();
            await foreach (var item in ReadReplayFramesAtAsync(selected, selectedSeconds, cancellationToken)) selectedFrames.Add(item);
            var sourceIdentity = replayContext is null ? new MatchReplaySourceIdentity("", "", 0, 0, new(null, null, null)) :
                new MatchReplaySourceIdentity(replayContext.FixtureVersion, replayContext.Provenance,
                    replayContext.PitchLengthMetres, replayContext.PitchWidthMetres,
                    replaySeed?.CompetitionRules ?? new(null, null, null));
            return new(selected, selectedEvents, replaySeed, replayContext, sourceIdentity, selectedReplayEvents, selectedFrames);
        }

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection selectedProjection,
            int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EventsRead.Add(item);
                yield return item;
                await Task.Yield();
            }
        }

        public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(replaySeed);

        public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult(replayContext);

        public async IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection selectedProjection,
            int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in replayEvents ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection selectedProjection,
            int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in replayFrames ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!enforceFrameTimeFilter || item.Frame.ClockSeconds <= selectedSeconds) yield return item;
                await Task.Yield();
            }
        }
    }
}
