using PitchMind.Domain.Match;

namespace PitchMind.Domain.Events;

public enum MatchEventType { KickOff, HalfTime, Pass, Shot, Goal, Foul, YellowCard, Substitution, Turnover, FullTime, Pressure, RedCard }

public sealed record MatchEvent(
    Guid EventId,
    MatchId MatchId,
    long Sequence,
    MatchEventType Type,
    int ClockSeconds,
    int Period,
    DateTimeOffset OccurredAt)
{
    public static MatchEvent Create(Guid eventId, MatchId matchId, long sequence,
        MatchEventType type, int clockSeconds, int period, DateTimeOffset occurredAt)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID required.", nameof(eventId));
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID required.", nameof(matchId));
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (clockSeconds < 0) throw new ArgumentOutOfRangeException(nameof(clockSeconds));
        if (period is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(period));
        return new(eventId, matchId, sequence, type, clockSeconds, period, occurredAt);
    }
}
