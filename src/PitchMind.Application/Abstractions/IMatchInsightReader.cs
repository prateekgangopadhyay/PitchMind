using PitchMind.Domain.Match;
using System.Text.Json;

namespace PitchMind.Application.Abstractions;

public sealed record PersistedEvidenceReference(string Kind, string Id, string Relation);

public sealed record PersistedMatchInsight(
    string InsightId,
    MatchId MatchId,
    int Revision,
    long AsOfSequence,
    string DetectorVersion,
    string TruthClass,
    string Summary,
    JsonElement Conditions,
    IReadOnlyList<string>? Limitations,
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
    long Sequence,
    int ClockSeconds,
    int Period,
    string PayloadJson);

/// <summary>Reads only persisted, revision-bound insights and their immutable evidence references.</summary>
public interface IMatchInsightReader
{
    Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, int maximumRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken);

    Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, int maximumRevision, int selectedSeconds,
        long throughSequence, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
        int maximumRevision, int selectedSeconds, long throughSequence, CancellationToken cancellationToken);
}
