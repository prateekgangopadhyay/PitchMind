using PitchMind.Domain.Events;

namespace PitchMind.Application.Matches;

/// <summary>Compares the complete canonical event payload, including its recorded timestamp offset.</summary>
public static class MatchEventPayloadEquality
{
    public static bool AreEqual(MatchEvent left, MatchEvent right) =>
        left == right && left.OccurredAt.EqualsExact(right.OccurredAt);
}
