namespace PitchMind.Application.Abstractions;

/// <summary>Application-owned projection state written by an idempotent downstream consumer.</summary>
public sealed record ConsumerProjectionSnapshot(
    Guid MessageId,
    Guid MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    long Sequence,
    int ClockSeconds,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256,
    string StateJson);
