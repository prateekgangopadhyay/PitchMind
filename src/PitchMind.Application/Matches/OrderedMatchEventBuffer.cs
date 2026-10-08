using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Matches;

public enum IngestionDisposition { Accepted, Buffered, Duplicate, Quarantined }

public sealed record IngestionResult(IngestionDisposition Disposition, IReadOnlyList<MatchEvent> Ready, string? Reason = null);

/// <summary>
/// Bounded, single-writer, per-match sequencing buffer. This is volatile state:
/// production consumers must checkpoint only after durable persistence (M05).
/// </summary>
public sealed class OrderedMatchEventBuffer
{
    private readonly MatchId _matchId;
    private readonly int _maxPending;
    private readonly SortedDictionary<long, MatchEvent> _pending = new();
    private readonly HashSet<Guid> _seenIds = new();
    private long _nextSequence;

    public OrderedMatchEventBuffer(MatchId matchId, long firstSequence = 0, int maxPending = 1024)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID required.", nameof(matchId));
        if (firstSequence < 0) throw new ArgumentOutOfRangeException(nameof(firstSequence));
        if (maxPending < 1) throw new ArgumentOutOfRangeException(nameof(maxPending));
        _matchId = matchId;
        _nextSequence = firstSequence;
        _maxPending = maxPending;
    }

    public long NextExpectedSequence => _nextSequence;
    public int PendingCount => _pending.Count;

    public IngestionResult Accept(MatchEvent matchEvent)
    {
        ArgumentNullException.ThrowIfNull(matchEvent);
        MatchEventValidator.Validate(matchEvent);
        if (matchEvent.MatchId != _matchId)
            return new(IngestionDisposition.Quarantined, Array.Empty<MatchEvent>(), "wrong_match");
        if (_seenIds.Contains(matchEvent.EventId))
            return new(IngestionDisposition.Duplicate, Array.Empty<MatchEvent>(), "duplicate_event_id");
        if (matchEvent.Sequence < _nextSequence)
            return new(IngestionDisposition.Quarantined, Array.Empty<MatchEvent>(), "stale_sequence");
        if (_pending.ContainsKey(matchEvent.Sequence))
            return new(IngestionDisposition.Quarantined, Array.Empty<MatchEvent>(), "conflicting_sequence");
        if (matchEvent.Sequence > _nextSequence && _pending.Count >= _maxPending)
            return new(IngestionDisposition.Quarantined, Array.Empty<MatchEvent>(), "pending_capacity_exceeded");

        _seenIds.Add(matchEvent.EventId);
        _pending.Add(matchEvent.Sequence, matchEvent);
        var ready = new List<MatchEvent>();
        while (_pending.Remove(_nextSequence, out var next))
        {
            ready.Add(next);
            _nextSequence++;
        }
        return new(ready.Count == 0 ? IngestionDisposition.Buffered : IngestionDisposition.Accepted, ready);
    }
}
