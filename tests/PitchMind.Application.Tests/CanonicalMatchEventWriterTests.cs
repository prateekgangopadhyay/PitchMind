using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class CanonicalMatchEventWriterTests
{
    private const string SourceSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task StoreFailureLeavesAggregateUnchangedAndRetryCommitsThenAdvancesIt()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var aggregate = CreateAggregate(fixture);
        var kickoff = fixture.Events[0];
        var store = new RecordingStore { FailureBeforeCommit = new IOException("database unavailable") };
        var writer = new CanonicalMatchEventWriter(store);

        await Assert.ThrowsAsync<IOException>(() => writer.AppendAsync(aggregate, kickoff, 1,
            "match-state-v1", SourceSha256, CancellationToken.None));

        Assert.Equal(-1, aggregate.Snapshot().LastSequence);
        Assert.Equal(MatchPhase.NotStarted, aggregate.Snapshot().Phase);

        var result = await writer.AppendAsync(aggregate, kickoff, 1,
            "match-state-v1", SourceSha256, CancellationToken.None);

        Assert.Equal(CanonicalUpdateCommitResult.Committed, result);
        Assert.Equal(0, aggregate.Snapshot().LastSequence);
        Assert.Equal(MatchPhase.FirstHalf, aggregate.Snapshot().Phase);
        Assert.Equal(0, store.Updates[^1].State.LastSequence);
        Assert.Equal(kickoff.Event.EventId, store.Updates[^1].Event.EventId);
    }

    [Fact]
    public async Task RetryAfterCommitWithLostAcknowledgementAppliesGoalOnce()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var goal = fixture.Events.First(item => item.Event.Type == MatchEventType.Goal);
        var aggregate = CreateAggregate(fixture);
        foreach (var prior in fixture.Events.TakeWhile(item => item.Event.Sequence < goal.Event.Sequence))
            aggregate.Apply(prior);

        var store = new RecordingStore { FailureAfterCommit = new IOException("acknowledgement lost") };
        var writer = new CanonicalMatchEventWriter(store);
        var revision = checked((int)goal.Event.Sequence + 1);

        await Assert.ThrowsAsync<IOException>(() => writer.AppendAsync(aggregate, goal, revision,
            "match-state-v1", SourceSha256, CancellationToken.None));
        Assert.Equal(goal.Event.Sequence - 1, aggregate.Snapshot().LastSequence);
        Assert.Single(store.PersistedUpdates);

        Assert.Equal(CanonicalUpdateCommitResult.AlreadyCommitted,
            await writer.AppendAsync(aggregate, goal, revision, "match-state-v1", SourceSha256, CancellationToken.None));

        var state = aggregate.Snapshot();
        Assert.Equal(goal.Event.Sequence, state.LastSequence);
        Assert.Equal(1, state.HomeScore + state.AwayScore);
        Assert.Equal(2, store.Updates.Count);
        Assert.Single(store.PersistedUpdates);
        Assert.Equal(store.Updates[0].State.MatchId, store.Updates[1].State.MatchId);
        Assert.Equal(store.Updates[0].State.ClockSeconds, store.Updates[1].State.ClockSeconds);
        Assert.Equal(store.Updates[0].State.HomeScore, store.Updates[1].State.HomeScore);
        Assert.Equal(store.Updates[0].State.AwayScore, store.Updates[1].State.AwayScore);
        Assert.Equal(store.Updates[0].State.Players, store.Updates[1].State.Players);
    }

    [Fact]
    public async Task InvalidGapIsRejectedBeforeStoreAndDoesNotChangeAggregate()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var aggregate = CreateAggregate(fixture);
        var store = new RecordingStore();
        var writer = new CanonicalMatchEventWriter(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(aggregate, fixture.Events[1], 2,
            "match-state-v1", SourceSha256, CancellationToken.None));

        Assert.Empty(store.Updates);
        Assert.Equal(-1, aggregate.Snapshot().LastSequence);
        Assert.Equal(MatchPhase.NotStarted, aggregate.Snapshot().Phase);
    }

    [Fact]
    public async Task CallsForSameAggregateAreSerialized()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var aggregate = CreateAggregate(fixture);
        var kickoff = fixture.Events[0];
        var store = new RecordingStore { Delay = TimeSpan.FromMilliseconds(25) };
        var firstWriter = new CanonicalMatchEventWriter(store);
        var secondWriter = new CanonicalMatchEventWriter(store);

        var first = firstWriter.AppendAsync(aggregate, kickoff, 1, "match-state-v1", SourceSha256, CancellationToken.None);
        var second = secondWriter.AppendAsync(aggregate, kickoff, 1, "match-state-v1", SourceSha256, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(0, aggregate.Snapshot().LastSequence);
        Assert.Equal(2, store.Updates.Count);
        Assert.All(store.Updates, update => Assert.Equal(0, update.State.LastSequence));
    }

    private static MatchAggregate CreateAggregate(SyntheticFixture fixture)
    {
        var players = fixture.Home.Players.Concat(fixture.Away.Players)
            .Select(player => (player.Id, player.TeamId));
        var active = fixture.Frames.Single(frame => frame.ClockSeconds == 0)
            .Players.Select(player => player.PlayerId);
        return new MatchAggregate(new MatchId(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id,
            players, active, fixture.Provenance);
    }

    private sealed class RecordingStore : ICanonicalUpdateStore
    {
        private readonly Dictionary<Guid, CanonicalUpdate> _committed = [];
        public List<CanonicalUpdate> Updates { get; } = [];
        public IReadOnlyList<CanonicalUpdate> PersistedUpdates => _committed.Values.ToArray();
        public Exception? FailureBeforeCommit { get; set; }
        public Exception? FailureAfterCommit { get; set; }
        public TimeSpan Delay { get; init; }

        public async Task<CanonicalUpdateCommitResult> CommitAsync(CanonicalUpdate update, CancellationToken cancellationToken)
        {
            if (FailureBeforeCommit is { } before)
            {
                FailureBeforeCommit = null;
                throw before;
            }
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            Updates.Add(update);
            if (_committed.TryGetValue(update.Event.EventId, out var prior))
            {
                if (prior.Event != update.Event || prior.State.LastSequence != update.State.LastSequence ||
                    prior.State.HomeScore != update.State.HomeScore || prior.State.AwayScore != update.State.AwayScore)
                    throw new InvalidOperationException("Conflicting event retry.");
                return CanonicalUpdateCommitResult.AlreadyCommitted;
            }
            _committed.Add(update.Event.EventId, update);
            if (FailureAfterCommit is { } after)
            {
                FailureAfterCommit = null;
                throw after;
            }
            return CanonicalUpdateCommitResult.Committed;
        }
    }
}
