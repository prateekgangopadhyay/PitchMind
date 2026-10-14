using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

/// <summary>Immutable source identity required before admitting a canonical match.</summary>
public sealed record MatchSourceMetadata(Guid MatchId, string FixtureVersion, string SourceVersion,
    string SourceSha256, string Provenance, decimal PitchLengthMetres, decimal PitchWidthMetres,
    string? CompetitionId = null, string? CompetitionRulesVersion = null, int? MaximumSubstituteReentriesPerPlayer = null);

/// <summary>A deterministic projection tied to its PostgreSQL admission revision and algorithm revision.</summary>
public sealed record PersistedMatchProjection(Guid MatchId, long CanonicalRevision, int Revision, long AsOfSequence,
    int AsOfClockSeconds, string AlgorithmVersion, string SourceSha256, MatchState State);

public sealed record InsightEvidenceReference(string Kind, string Id, string Relation);

/// <summary>Immutable derived insight with evidence references; never a canonical observed event.</summary>
public sealed record CanonicalInsightRecord(string InsightId, Guid MatchId, long CanonicalRevision, int Revision,
    long AsOfSequence, string DetectorVersion, string TruthClass, string Summary,
    string ConditionsJson, IReadOnlyList<InsightEvidenceReference> Evidence);

public sealed record InsightProvenanceRecord(CanonicalInsightRecord Insight,
    MatchSourceMetadata Source, PersistedMatchProjection Projection, string StateSha256);

/// <summary>Read access to canonical projections and provenance; projection writes remain atomic in ICanonicalUpdateStore.</summary>
public interface IMatchProjectionStore
{
    Task SaveMatchMetadataAsync(MatchSourceMetadata metadata, CancellationToken cancellationToken);
    Task<PersistedMatchProjection?> ReadProjectionAsync(Guid matchId, long canonicalRevision, int projectionRevision,
        CancellationToken cancellationToken);
    Task<PersistedMatchProjection?> ReadProjectionAtTimeAsync(Guid matchId, long canonicalRevision, int atClockSeconds,
        CancellationToken cancellationToken);
    Task<InsightProvenanceRecord?> ReadInsightProvenanceAsync(string insightId, CancellationToken cancellationToken);
}
