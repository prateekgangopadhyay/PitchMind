using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public interface IMatchEventStore
{
    Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken);
    IAsyncEnumerable<MatchEvent> ReadAsync(MatchId matchId, CancellationToken cancellationToken);
}
