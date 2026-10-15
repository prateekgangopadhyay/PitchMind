using System.Text;

namespace PitchMind.Application.MatchIntelligence;

public enum ExplanationVerificationFailure
{
    InvalidCandidate,
    ContextMismatch,
    UnsupportedEvidenceReference
}

public enum ExplanationVerificationDisposition
{
    EvidenceOnlyReformulation
}

public sealed record VerifiedExplanationClaim(string Text, IReadOnlyList<string> EvidenceIds);

/// <summary>Safe application result. Model-authored prose is never copied into this contract.</summary>
public sealed record VerifiedExplanation(
    string Text,
    IReadOnlyList<VerifiedExplanationClaim> Claims,
    IReadOnlyList<string> Limitations,
    string MatchId,
    long CanonicalRevision,
    int SelectedTimeSeconds,
    ExplanationAudience Audience,
    string Source,
    ExplanationVerificationDisposition Disposition,
    string VerifierVersion);

public sealed record ExplanationVerificationResult(
    VerifiedExplanation? Explanation,
    ExplanationVerificationFailure? Failure)
{
    public bool Succeeded => Explanation is not null && Failure is null;
    public static ExplanationVerificationResult Success(VerifiedExplanation explanation) => new(explanation, null);
    public static ExplanationVerificationResult Refused(ExplanationVerificationFailure failure) => new(null, failure);
}

/// <summary>
/// Verifies candidate metadata/citations against the selected packet and reforms the user-facing response
/// from safe packet metadata only. Candidate-authored text and claim strings are deliberately discarded.
/// </summary>
public sealed class ExplanationCandidateVerifier
{
    private const string PolicyVersion = "explanation-candidate-verifier-v1";
    private static readonly string[] Limitations =
    [
        "This response names only the selected insight metadata and its evidence references.",
        "Evidence references do not establish causation or coach intent.",
        "Review the cited canonical evidence for the underlying football actions."
    ];

    public ExplanationVerificationResult Verify(
        CandidateExplanation? candidate,
        MatchIntelligenceContextPacket? context,
        ExplanationAudience audience)
    {
        if (candidate is null || context is null || !Enum.IsDefined(audience) ||
            string.IsNullOrWhiteSpace(candidate.Text) || candidate.Text.Length > 2_500 || candidate.Text.Any(char.IsControl) ||
            !IsSafeIdentifier(candidate.ModelVersion, 128) || !IsSafeIdentifier(candidate.TraceId, 128) ||
            candidate.Duration < TimeSpan.Zero || candidate.InputTokens is < 0 || candidate.OutputTokens is < 0 ||
            candidate.Claims is null || candidate.Claims.Count is < 1 or > 12)
            return ExplanationVerificationResult.Refused(ExplanationVerificationFailure.InvalidCandidate);

        if (context.Claim is null || !Enum.IsDefined(context.Claim.Category) ||
            !IsSafeIdentifier(context.MatchId, 64) || candidate.MatchId != context.MatchId || candidate.CanonicalRevision != context.CanonicalRevision ||
            candidate.SelectedTimeSeconds != context.SelectedTimeSeconds || context.CanonicalRevision <= 0 ||
            context.SelectedTimeSeconds is < 0 or > 5400 || context.ProjectionAsOfTimeSeconds is < 0 ||
            context.ProjectionAsOfTimeSeconds > context.SelectedTimeSeconds || context.ThroughSequence < 0 ||
            context.Evidence is null || context.Evidence.Count is < 1 or > 12 ||
            context.Evidence.Any(item => item is null || item.Kind is not ("event" or "frame") ||
                string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 256 || item.Id.Any(char.IsControl)) ||
            context.Evidence.Any(item => item.Sequence < 0 || item.Sequence > context.ThroughSequence ||
                item.ClockSeconds < 0 || item.ClockSeconds > context.SelectedTimeSeconds || item.Period is < 1 or > 5 ||
                string.IsNullOrWhiteSpace(item.PayloadJson) || Encoding.UTF8.GetByteCount(item.PayloadJson) > 6_144 ||
                string.IsNullOrWhiteSpace(item.TrustBoundary) || item.TrustBoundary.Length > 256 ||
                string.IsNullOrWhiteSpace(item.Relation) || item.Relation.Length > 128) ||
            context.Evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != context.Evidence.Count)
            return ExplanationVerificationResult.Refused(ExplanationVerificationFailure.ContextMismatch);

        var evidenceIds = context.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var claim in candidate.Claims)
        {
            if (claim is null || string.IsNullOrWhiteSpace(claim.Text) || claim.Text.Length > 1_000 || claim.Text.Any(char.IsControl) ||
                claim.EvidenceIds is null || claim.EvidenceIds.Count is < 1 or > 12 ||
                claim.EvidenceIds.Any(id => string.IsNullOrWhiteSpace(id) || !evidenceIds.Contains(id)))
                return ExplanationVerificationResult.Refused(ExplanationVerificationFailure.UnsupportedEvidenceReference);
        }

        var referencedIds = candidate.Claims.SelectMany(claim => claim.EvidenceIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (referencedIds.Length == 0)
            return ExplanationVerificationResult.Refused(ExplanationVerificationFailure.UnsupportedEvidenceReference);

        var clock = TimeSpan.FromSeconds(context.SelectedTimeSeconds);
        var time = $"{(int)clock.TotalMinutes:00}:{clock.Seconds:00}";
        var evidenceTypes = context.Evidence.Where(item => referencedIds.Contains(item.Id, StringComparer.Ordinal))
            .Select(item => item.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var text = audience switch
        {
            ExplanationAudience.Fan => $"At {time}, the selected insight is classified as {FormatCategory(context.Claim.Category)}. " +
                                       $"It references {referencedIds.Length} canonical evidence item(s) ({string.Join("/", evidenceTypes)}). " +
                                       "This evidence does not prove cause or what the coach intended.",
            ExplanationAudience.Analyst => $"At {time} (revision {context.CanonicalRevision}), the selected insight is classified as " +
                                           $"{FormatCategory(context.Claim.Category)} and references {referencedIds.Length} " +
                                           $"canonical evidence item(s) ({string.Join("/", evidenceTypes)}). " +
                                           "The evidence does not support causal or coach-intent attribution.",
            _ => throw new ArgumentOutOfRangeException(nameof(audience))
        };
        var verifiedClaim = new VerifiedExplanationClaim(text, Array.AsReadOnly(referencedIds));
        var result = new VerifiedExplanation(text, Array.AsReadOnly(new[] { verifiedClaim }),
            Array.AsReadOnly(Limitations.ToArray()), context.MatchId, context.CanonicalRevision,
            context.SelectedTimeSeconds, audience, "deterministic-evidence-reformulation",
            ExplanationVerificationDisposition.EvidenceOnlyReformulation, PolicyVersion);
        return ExplanationVerificationResult.Success(result);
    }

    private static string FormatCategory(MatchIntelligenceClaimCategory category) => category switch
    {
        MatchIntelligenceClaimCategory.EvidenceSupportedPattern => "an evidence-supported pattern",
        MatchIntelligenceClaimCategory.SupportedInterpretation => "a supported interpretation",
        MatchIntelligenceClaimCategory.Hypothesis => "a hypothesis",
        _ => throw new ArgumentOutOfRangeException(nameof(category), PolicyVersion)
    };

    private static bool IsSafeIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
}
