namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Evidence that an input was rejected or held. The current adapter only appends; this record does not
/// determine retry, replay, or reconciliation behavior.
/// </summary>
public sealed record QuarantineRecord(
    Guid RecordId,
    Guid? MatchId,
    string SourcePointer,
    string? PayloadHash,
    string Reason,
    DateTimeOffset ObservedAt);
