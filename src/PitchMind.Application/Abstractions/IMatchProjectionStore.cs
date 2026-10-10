using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

/// <summary>Immutable source identity required before admitting a canonical match.</summary>
public sealed record MatchSourceMetadata(Guid MatchId, string FixtureVersion, string SourceVersion,
    string SourceSha256, string Provenance, decimal PitchLengthMetres, decimal PitchWidthMetres);

/// <summary>A deterministic match-state snapshot at an immutable source revision and sequence.</summary>
public sealed record PersistedMatchProjection(Guid MatchId, int Revision, long AsOfSequence,
    int AsOfClockSeconds, string AlgorithmVersion, string SourceSha256, MatchState State);

public sealed record InsightEvidenceReference(string Kind, string Id, string Relation);

/// <summary>Immutable derived insight with evidence references; never a canonical observed event.</summary>
public sealed record CanonicalInsightRecord(string InsightId, Guid MatchId, int Revision,
    long AsOfSequence, string DetectorVersion, string TruthClass, string Summary,
    string ConditionsJson, IReadOnlyList<InsightEvidenceReference> Evidence);

public sealed record InsightProvenanceRecord(CanonicalInsightRecord Insight,
    MatchSourceMetadata Source, PersistedMatchProjection Projection, string StateSha256);

/// <summary>Read access to canonical projections and provenance; projection writes remain atomic in ICanonicalUpdateStore.</summary>
public interface IMatchProjectionStore
{
    Task SaveMatchMetadataAsync(MatchSourceMetadata metadata, CancellationToken cancellationToken);
    Task<PersistedMatchProjection?> ReadProjectionAsync(Guid matchId, int revision, CancellationToken cancellationToken);
    Task<InsightProvenanceRecord?> ReadInsightProvenanceAsync(string insightId, CancellationToken cancellationToken);
}
