using PitchMind.Domain.Events;

namespace PitchMind.Application.Matches;

public static class MatchEventValidator
{
    public static void Validate(MatchEvent matchEvent)
    {
        if (matchEvent.EventId == Guid.Empty) throw new ArgumentException("Event ID is required.");
        if (matchEvent.MatchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.");
        if (matchEvent.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(matchEvent));
        if (matchEvent.ClockSeconds < 0) throw new ArgumentOutOfRangeException(nameof(matchEvent));
    }
}
