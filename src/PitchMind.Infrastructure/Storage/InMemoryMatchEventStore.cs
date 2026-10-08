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
        if (!matchEvents.TryAdd(matchEvent.EventId, matchEvent))
            throw new InvalidOperationException("Duplicate event ID.");
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
