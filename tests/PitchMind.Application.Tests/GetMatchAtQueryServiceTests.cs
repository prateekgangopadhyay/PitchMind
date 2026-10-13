using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using System.Runtime.CompilerServices;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class GetMatchAtQueryServiceTests
{
    [Fact]
    public async Task ComposesProjectionAndOnlyItsSelectedTimeEventWindow()
    {
        var matchId = MatchId.New();
        var projection = Projection(matchId, canonicalRevision: 2, projectionRevision: 2, sequence: 1, clock: 3308);
        var events = new[] { Event(matchId, 0, 0), Event(matchId, 1, 3308) };
        var reader = new ProjectionReaderFake(projection, events);
        var query = new GetMatchAtQuery(matchId, 3308, 3, "aggregate-v1");

        var result = await new GetMatchAtQueryService(reader).ExecuteAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(query.SelectedSeconds, result.SelectedSeconds);
        Assert.Equal(2, result.Projection.CanonicalRevision);
        Assert.Equal(2, result.Projection.ProjectionRevision);
        Assert.Equal(new long[] { 0, 1 }, result.Events.Select(item => item.Sequence));
        Assert.Equal(query, reader.LastQuery);
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
        IReadOnlyList<MatchEvent> events) : IMatchProjectionReader
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
    }
}
