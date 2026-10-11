namespace PitchMind.Application.MatchIntelligence;

public enum ExplanationAudience
{
    Fan,
    Analyst
}

public sealed record ExplanationGenerationRequest(
    MatchIntelligenceContextPacket Context,
    ExplanationAudience Audience,
    string Question,
    int MaximumOutputTokens = 256);

public sealed record CandidateExplanationClaim(string Text, IReadOnlyList<string> EvidenceIds);

/// <summary>Untrusted model candidate. It is not a user-facing or canonical result until verified by EP08-S04.</summary>
public sealed record CandidateExplanation(
    string Text,
    IReadOnlyList<CandidateExplanationClaim> Claims,
    string ModelVersion,
    string TraceId,
    int? InputTokens,
    int? OutputTokens,
    TimeSpan Duration,
    string MatchId,
    int CanonicalRevision,
    int SelectedTimeSeconds);

public enum ExplanationGenerationFailure
{
    Unavailable,
    InvalidCandidate,
    RequestTooLarge
}

public sealed record ExplanationGenerationResult(CandidateExplanation? Candidate, ExplanationGenerationFailure? Failure)
{
    public bool Succeeded => Candidate is not null && Failure is null;
    public static ExplanationGenerationResult Success(CandidateExplanation candidate) => new(candidate, null);
    public static ExplanationGenerationResult Refused(ExplanationGenerationFailure failure) => new(null, failure);
}

/// <summary>Application port for generating an untrusted explanation candidate from bounded evidence.</summary>
public interface IExplanationCandidateGenerator
{
    Task<ExplanationGenerationResult> GenerateAsync(ExplanationGenerationRequest request, CancellationToken cancellationToken);
}
