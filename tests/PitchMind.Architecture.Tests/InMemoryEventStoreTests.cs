using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Architecture.Tests;

public sealed class InMemoryEventStoreTests
{
    [Xunit.Fact]
    public async Task RetriesAreIdempotentAndConflictsAreRejected()
    {
        var store = new InMemoryMatchEventStore();
        var input = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.AppendAsync(input, default)));
        var events = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(input.MatchId, default)) events.Add(item);
        Xunit.Assert.Single(events);
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(input with { Type = MatchEventType.Goal }, default));
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(input with { EventId = Guid.NewGuid() }, default));
    }

    [Xunit.Fact]
    public async Task GapsRequireReconciliationBeforeHistoryAdvances()
    {
        var store = new InMemoryMatchEventStore();
        var input = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
        await store.AppendAsync(input, default);
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(input with { EventId = Guid.NewGuid(), Sequence = 2 }, default));
        await store.AppendAsync(input with { EventId = Guid.NewGuid(), Sequence = 1, Type = MatchEventType.Pass }, default);
    }
}
