namespace PitchMind.Application.BackgroundJobs;

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
