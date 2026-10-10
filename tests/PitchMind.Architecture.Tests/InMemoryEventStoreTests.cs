using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Application.Matches;
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
        var conflictingId = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(input with { Type = MatchEventType.Goal }, default));
        Xunit.Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", conflictingId.Code);
        Xunit.Assert.Equal(MatchEventIngestionDisposition.Quarantine, conflictingId.Disposition);

        var conflictingSequence = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(input with { EventId = Guid.NewGuid() }, default));
        Xunit.Assert.Equal("SEQUENCE_ALREADY_USED", conflictingSequence.Code);
        Xunit.Assert.Equal(MatchEventIngestionDisposition.Quarantine, conflictingSequence.Disposition);
        events.Clear();
        await foreach (var item in store.ReadAsync(input.MatchId, default)) events.Add(item);
        Xunit.Assert.Equal(input, Xunit.Assert.Single(events));
    }

    [Xunit.Fact]
    public async Task GapsRequireReconciliationBeforeHistoryAdvances()
    {
        var store = new InMemoryMatchEventStore();
        var input = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
        await store.AppendAsync(input, default);
        var gap = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(input with { EventId = Guid.NewGuid(), Sequence = 2 }, default));
        Xunit.Assert.Equal("SEQUENCE_GAP", gap.Code);
        Xunit.Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, gap.Disposition);
        Xunit.Assert.Equal<long?>(1, gap.ExpectedSequence);
        Xunit.Assert.Equal(2L, gap.ReceivedSequence);
        await store.AppendAsync(input with { EventId = Guid.NewGuid(), Sequence = 1, Type = MatchEventType.Pass }, default);

        var events = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(input.MatchId, default)) events.Add(item);
        Xunit.Assert.Equal(new[] { 0L, 1L }, events.Select(item => item.Sequence));
    }

    [Xunit.Fact]
    public async Task EventIdsAreGlobalAcrossMatchesAndAConflictDoesNotMutateEitherLedger()
    {
        var store = new InMemoryMatchEventStore();
        var eventId = Guid.NewGuid();
        var first = MatchEvent.Create(eventId, MatchId.New(), 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
        var otherMatch = first with { MatchId = MatchId.New() };
        await store.AppendAsync(first, default);

        var conflict = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(() => store.AppendAsync(otherMatch, default));
        Xunit.Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", conflict.Code);
        Xunit.Assert.Equal(MatchEventIngestionDisposition.Quarantine, conflict.Disposition);

        var firstEvents = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(first.MatchId, default)) firstEvents.Add(item);
        var otherEvents = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(otherMatch.MatchId, default)) otherEvents.Add(item);
        Xunit.Assert.Equal(first, Xunit.Assert.Single(firstEvents));
        Xunit.Assert.Empty(otherEvents);
    }

    [Xunit.Fact]
    public async Task OffsetDistinctTimestampForSameInstantIsAConflict()
    {
        var store = new InMemoryMatchEventStore();
        var original = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1,
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(original, default);
        var offsetDistinct = original with { OccurredAt = original.OccurredAt.ToOffset(TimeSpan.FromHours(1)) };

        var conflict = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(offsetDistinct, default));

        Xunit.Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", conflict.Code);
        var events = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(original.MatchId, default)) events.Add(item);
        Xunit.Assert.True(Xunit.Assert.Single(events).OccurredAt.EqualsExact(original.OccurredAt));
    }

    [Xunit.Fact]
    public async Task InvalidEventGetsStableQuarantineClassification()
    {
        var store = new InMemoryMatchEventStore();
        var invalid = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch)
            with { Type = (MatchEventType)999 };

        var exception = await Xunit.Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(invalid, default));

        Xunit.Assert.Equal("INVALID_EVENT_PAYLOAD", exception.Code);
        Xunit.Assert.Equal(MatchEventIngestionDisposition.Quarantine, exception.Disposition);
        var events = new List<MatchEvent>();
        await foreach (var item in store.ReadAsync(invalid.MatchId, default)) events.Add(item);
        Xunit.Assert.Empty(events);
    }

    [Xunit.Fact]
    public async Task NullEventIsRejectedWithoutDereferencing()
    {
        var store = new InMemoryMatchEventStore();
        await Xunit.Assert.ThrowsAsync<ArgumentNullException>(
            () => store.AppendAsync(null!, default));
    }
}
