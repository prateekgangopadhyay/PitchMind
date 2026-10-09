using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

// M01 developer-only adapter. M05 replaces this with canonical PostgreSQL storage.
public sealed class InMemoryMatchEventStore : IMatchEventStore
{
    private readonly ConcurrentDictionary<MatchId, ConcurrentDictionary<Guid, MatchEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, MatchId> _eventOwners = new();
    private readonly ConcurrentDictionary<Guid, object> _eventLocks = new();

    public Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateForIngestion(matchEvent);
        var eventLock = _eventLocks.GetOrAdd(matchEvent.EventId, _ => new object());
        lock (eventLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchEvents = _events.GetOrAdd(matchEvent.MatchId, _ => new());
            lock (matchEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_eventOwners.TryGetValue(matchEvent.EventId, out var owner) && owner != matchEvent.MatchId)
                    throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                        MatchEventIngestionFailure.EventIdPayloadConflict, null, matchEvent.Sequence);
                if (matchEvents.TryGetValue(matchEvent.EventId, out var existing))
                {
                    if (!MatchEventPayloadEquality.AreEqual(existing, matchEvent))
                        throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                            MatchEventIngestionFailure.EventIdPayloadConflict, GetExpectedSequence(matchEvents), matchEvent.Sequence);
                    return Task.CompletedTask;
                }
                var expected = GetExpectedSequence(matchEvents);
                if (matchEvents.Values.Any(e => e.Sequence == matchEvent.Sequence))
                    throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                        MatchEventIngestionFailure.SequenceAlreadyUsed, expected, matchEvent.Sequence);
                if (matchEvent.Sequence > expected)
                    throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                        MatchEventIngestionFailure.SequenceGap, expected, matchEvent.Sequence);
                if (matchEvent.Sequence < expected)
                    throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                        MatchEventIngestionFailure.OutOfOrder, expected, matchEvent.Sequence);
                matchEvents[matchEvent.EventId] = matchEvent;
                _eventOwners[matchEvent.EventId] = matchEvent.MatchId;
            }
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<MatchEvent> ReadAsync(MatchId matchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_events.TryGetValue(matchId, out var events))
        {
            foreach (var matchEvent in events.Values.OrderBy(e => e.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return matchEvent;
            }
        }
        await Task.CompletedTask;
    }

    private static long GetExpectedSequence(ConcurrentDictionary<Guid, MatchEvent> matchEvents) =>
        matchEvents.IsEmpty ? 0 : matchEvents.Values.Max(e => e.Sequence) + 1;

    private static void ValidateForIngestion(MatchEvent matchEvent)
    {
        ArgumentNullException.ThrowIfNull(matchEvent);
        try { MatchEventValidator.Validate(matchEvent); }
        catch (ArgumentException exception)
        {
            throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                MatchEventIngestionFailure.InvalidEventPayload, null, matchEvent.Sequence, exception);
        }
    }
}
