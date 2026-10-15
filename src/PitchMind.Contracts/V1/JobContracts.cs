namespace PitchMind.Contracts.V1;

public enum JobExecutionOutcome
{
    Completed,
    RetryableFailure,
    PoisonDeadLetter
}

public sealed record BackgroundJobEnvelope<T>(
    string JobId,
    string JobType,
    string MatchId,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset EnqueuedUtc,
    string TraceCorrelationId,
    T Payload);

public sealed record AutoRecapJobPayload(
    string MatchId,
    long CanonicalRevision,
    int FinalScoreHome,
    int FinalScoreAway,
    int DurationSeconds,
    string Provenance);

public sealed record EvidenceExportJobPayload(
    string MatchId,
    long CanonicalRevision,
    int SelectedTimeSeconds,
    string RequestedByUserId,
    string RequestedRole,
    int ExpirationSeconds);

public sealed record AutoRecapResponse(
    string MatchId,
    long CanonicalRevision,
    string Headline,
    string Summary,
    IReadOnlyList<string> KeyMomentsSummary,
    string Provenance,
    bool GeneratedByAi,
    DateTimeOffset GeneratedUtc);

public sealed record EvidenceExportResponse(
    string ExportId,
    string MatchId,
    long CanonicalRevision,
    int SelectedTimeSeconds,
    string FixtureVersion,
    string SourceSha256,
    string StateSha256,
    string AlgorithmVersion,
    int EvidenceItemCount,
    string Signature,
    DateTimeOffset ExpiresAtUtc);
