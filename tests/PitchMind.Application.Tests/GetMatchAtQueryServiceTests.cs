using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Xunit;

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
        var reader = new ProjectionReaderFake(projection, events, aggregate.ReplaySeed, [kickoff, pass]);
        var query = new GetMatchAtQuery(matchId, 3308, 3, "aggregate-v1");

        var result = await new GetMatchAtQueryService(reader).ExecuteAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(query.SelectedSeconds, result.SelectedSeconds);
        Assert.Equal(2, result.Projection.CanonicalRevision);
        Assert.Equal(2, result.Projection.ProjectionRevision);
        Assert.Equal(new long[] { 0, 1 }, result.Events.Select(item => item.Sequence));
        Assert.Equal(query, reader.LastQuery);
        Assert.Equal(stateHash, result.ReplaySha256);
        Assert.Equal(stateJson, JsonSerializer.Serialize(result.ReplayedState, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task MissingProjectionReturnsNoMatchResult()
    {
        var reader = new ProjectionReaderFake(null, []);
        var result = await new GetMatchAtQueryService(reader).ExecuteAsync(
            new(MatchId.New(), 10, 1, "aggregate-v1"), CancellationToken.None);

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
            new(matchId, 0, 1, "aggregate-v1"), CancellationToken.None));
        Assert.Empty(reader.EventsRead);
    }

    [Fact]
    public async Task RejectsFutureEventsReturnedByAReaderAdapter()
    {
        var matchId = MatchId.New();
        var reader = new ProjectionReaderFake(Projection(matchId, 2, 2, 1, 3308),
            [Event(matchId, 0, 0), Event(matchId, 1, 3309)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1"), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNonContiguousEventSequences()
    {
        var matchId = MatchId.New();
        var projection = Projection(matchId, 2, 2, 1, 3308);
        var reader = new ProjectionReaderFake(projection,
            [Event(matchId, 0, 3307), Event(matchId, 2, 3308)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1"), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsBackwardsEventClocks()
    {
        var matchId = MatchId.New();
        var projection = Projection(matchId, 2, 2, 1, 3308);
        var reader = new ProjectionReaderFake(projection,
            [Event(matchId, 0, 3308), Event(matchId, 1, 3307)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetMatchAtQueryService(reader).ExecuteAsync(
            new(matchId, 3308, 2, "aggregate-v1"), CancellationToken.None));
    }

    private static SelectedMatchProjection Projection(MatchId matchId, long canonicalRevision,
        int projectionRevision, long sequence, int clock) => new(
        new MatchState(matchId.ToString(), clock, 1, MatchPhase.FirstHalf, 0, 0, null, 11, 11, [], sequence, "synthetic"),
        canonicalRevision, projectionRevision, sequence, clock, "aggregate-v1", new string('a', 64), new string('b', 64));

    private static MatchEvent Event(MatchId matchId, long sequence, int clock) =>
        MatchEvent.Create(Guid.NewGuid(), matchId, sequence, MatchEventType.Pass, clock, 1, DateTimeOffset.UnixEpoch.AddSeconds(clock));

    private sealed class ProjectionReaderFake(SelectedMatchProjection? projection,
        IReadOnlyList<MatchEvent> events, MatchReplaySeed? replaySeed = null,
        IReadOnlyList<SimulationEvent>? replayEvents = null) : IMatchProjectionReader
    {
        public GetMatchAtQuery? LastQuery { get; private set; }
        public List<MatchEvent> EventsRead { get; } = [];

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
        {
            LastQuery = new(matchId, selectedSeconds, upToCanonicalRevision, algorithmVersion);
            return Task.FromResult(projection);
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
    }
}
