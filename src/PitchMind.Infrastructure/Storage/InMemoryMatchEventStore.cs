using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

// M01 developer-only adapter. M05 replaces this with canonical PostgreSQL storage.
public sealed class InMemoryMatchEventStore : IMatchEventStore
{
    private readonly ConcurrentDictionary<MatchId, ConcurrentDictionary<Guid, MatchEvent>> _events = new();

    public Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matchEvents = _events.GetOrAdd(matchEvent.MatchId, _ => new());
        lock (matchEvents)
        {
            if (matchEvents.TryGetValue(matchEvent.EventId, out var existing))
            {
                if (existing != matchEvent) throw new InvalidOperationException("Conflicting payload for existing event ID.");
                return Task.CompletedTask;
            }
            if (matchEvents.Values.Any(e => e.Sequence == matchEvent.Sequence))
                throw new InvalidOperationException("Sequence already belongs to another event.");
            var expected = matchEvents.IsEmpty ? 0 : matchEvents.Values.Max(e => e.Sequence) + 1;
            if (matchEvent.Sequence != expected)
                throw new InvalidOperationException("Sequence gap or out-of-order event; reconciliation required.");
            matchEvents[matchEvent.EventId] = matchEvent;
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
}
