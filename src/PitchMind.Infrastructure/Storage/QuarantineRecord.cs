namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Evidence that an input was rejected or held. The current adapter only appends; this record does not
/// determine retry, replay, or reconciliation behavior.
/// </summary>
public sealed record QuarantineRecord(
    Guid RecordId,
    Guid? MatchId,
    string? SourcePointer,
    string? PayloadHash,
    string Reason,
    DateTimeOffset ObservedAt)
{
    private readonly DateTimeOffset _observedAt = NormalizeTimestamp(ObservedAt);

    public DateTimeOffset ObservedAt
    {
        get => _observedAt;
        init => _observedAt = NormalizeTimestamp(value);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var subMicroseconds = value.UtcTicks % TimeSpan.TicksPerMicrosecond;
        if (subMicroseconds < 0) subMicroseconds += TimeSpan.TicksPerMicrosecond;
        return value.AddTicks(-subMicroseconds);
    }
}
