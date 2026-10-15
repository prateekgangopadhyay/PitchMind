using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public sealed record PersistedEvidenceReference(string Kind, string Id, string Relation);

public sealed record PersistedMatchInsight(
    string InsightId,
    MatchId MatchId,
    long CanonicalRevision,
    int Revision,
    long AsOfSequence,
    string DetectorVersion,
    string TruthClass,
    string Summary,
    string ConditionsJson,
    string SourceVersion,
    string Provenance,
    string SourceSha256,
    IReadOnlyList<PersistedEvidenceReference> Evidence);

public sealed record PersistedMatchInsightPage(
    IReadOnlyList<PersistedMatchInsight> Insights,
    long? NextAfterSequence,
    string? NextAfterInsightId,
    bool HasMore);

public sealed record PersistedCanonicalEvidenceItem(
    string Kind,
    string Id,
    string Relation,
    long SourceCanonicalRevision,
    long Sequence,
    int ClockSeconds,
    int Period,
    string PayloadJson);

/// <summary>Reads only persisted, revision-bound insights and their immutable evidence references.</summary>
public interface IMatchInsightReader
{
    Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, long maximumCanonicalRevision,
        int maximumProjectionRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken);

    Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, long maximumCanonicalRevision,
        int maximumProjectionRevision, int selectedSeconds,
        long throughSequence, CancellationToken cancellationToken, int maximumEvidenceItems = 100);

    Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
        long maximumCanonicalRevision, int maximumProjectionRevision, int selectedSeconds, long throughSequence,
        CancellationToken cancellationToken, int maximumItems = 100, int maximumItemBytes = 65_536);
}
