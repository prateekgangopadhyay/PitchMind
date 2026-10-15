namespace PitchMind.Application.Matches;

/// <summary>
/// Sanitized evidence for one rejected source observation. This contains only a source locator,
/// optional digest, and stable reason code; raw source or canonical payload is never included.
/// </summary>
public sealed record QuarantineEvidence(
    Guid RecordId,
    Guid? MatchId,
    string SourcePointer,
    string? PayloadHash,
    string Reason,
    DateTimeOffset ObservedAt);

/// <summary>Caller-supplied source identity and digest; neither value is inferred from a canonical event.</summary>
public sealed record IngestionSourceReference(string SourcePointer, string? PayloadHash);
