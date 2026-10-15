using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PitchMind.Application.MatchIntelligence;

public enum CandidateExplanationLineageFailure
{
    None,
    InvalidContext,
    ContextMismatch,
    InvalidCandidate,
    MissingClaimEvidence,
    UnknownEvidence,
    EvidenceOutsideSelectedContext,
    NarrativeDoesNotMatchClaims
}

/// <summary>
/// Result of deterministic bounds and lineage checks only. A passing result is still an untrusted
/// model candidate; it is not a semantically verified or publishable explanation.
/// </summary>
public sealed record CandidateExplanationLineageCheck(
    bool IsLineageBound,
    CandidateExplanationLineageFailure Failure)
{
    /// <summary>Lineage checks never authorize display, persistence, or canonical writes.</summary>
    public bool CanBePublished => false;

    public static CandidateExplanationLineageCheck Pass() => new(true, CandidateExplanationLineageFailure.None);
    public static CandidateExplanationLineageCheck Refuse(CandidateExplanationLineageFailure failure) => new(false, failure);
}

/// <summary>
/// Applies deterministic size, packet-identity, time/sequence, and evidence-ID checks to a model
/// candidate. It deliberately does not claim that cited evidence semantically entails candidate text.
/// </summary>
public sealed class CandidateExplanationLineageChecker
{
    private const int MaximumClaims = 12;
    private const int MaximumClaimBytes = 2_048;
    private const int MaximumNarrativeBytes = 6_144;
    private const int MaximumEvidenceItems = 12;
    private const int MaximumEvidencePayloadBytes = 6_144;
    private static readonly Regex Sha256Pattern = new("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);

    public CandidateExplanationLineageCheck Check(
        CandidateExplanation? candidate,
        MatchIntelligenceContextPacket? context)
    {
        if (!IsValidContext(context))
            return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.InvalidContext);
        if (candidate is null)
            return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.InvalidCandidate);

        if (!MatchesContext(candidate, context!))
            return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.ContextMismatch);
        if (string.IsNullOrWhiteSpace(candidate.Text) ||
            Encoding.UTF8.GetByteCount(candidate.Text) > MaximumNarrativeBytes ||
            candidate.Claims is null || candidate.Claims.Count is < 1 or > MaximumClaims ||
            string.IsNullOrWhiteSpace(candidate.ModelVersion) || candidate.ModelVersion.Length > 256 ||
            string.IsNullOrWhiteSpace(candidate.TraceId) || candidate.TraceId.Length > 256 ||
            candidate.InputTokens is < 0 || candidate.OutputTokens is < 0 || candidate.Duration < TimeSpan.Zero)
            return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.InvalidCandidate);

        var evidenceById = context!.Evidence.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var claimTexts = new List<string>(candidate.Claims.Count);
        foreach (var claim in candidate.Claims)
        {
            if (claim is null || string.IsNullOrWhiteSpace(claim.Text) ||
                Encoding.UTF8.GetByteCount(claim.Text) > MaximumClaimBytes)
                return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.InvalidCandidate);
            if (claim.EvidenceIds is null || claim.EvidenceIds.Count == 0)
                return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.MissingClaimEvidence);

            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var evidenceId in claim.EvidenceIds)
            {
                if (string.IsNullOrWhiteSpace(evidenceId) || !uniqueIds.Add(evidenceId))
                    return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.UnknownEvidence);
                if (!evidenceById.TryGetValue(evidenceId, out var evidence))
                    return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.UnknownEvidence);
                if (!IsVisibleEvidence(evidence, context))
                    return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.EvidenceOutsideSelectedContext);
            }

            claimTexts.Add(claim.Text.Trim());
        }

        var assembledNarrative = string.Join(" ", claimTexts);
        if (!string.Equals(candidate.Text.Trim(), assembledNarrative, StringComparison.Ordinal))
            return CandidateExplanationLineageCheck.Refuse(CandidateExplanationLineageFailure.NarrativeDoesNotMatchClaims);

        return CandidateExplanationLineageCheck.Pass();
    }

    private static bool IsValidContext(MatchIntelligenceContextPacket? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.MatchId) || context.MatchId.Length > 128 ||
            context.CanonicalRevision <= 0 || context.ProjectionRevision <= 0 ||
            context.SelectedTimeSeconds < 0 || context.ProjectionAsOfTimeSeconds < 0 ||
            context.ProjectionAsOfTimeSeconds > context.SelectedTimeSeconds || context.ThroughSequence < 0 ||
            context.Score is null || context.Score.Home < 0 || context.Score.Away < 0 ||
            !IsBounded(context.SourceVersion, 256) || !IsBounded(context.Provenance, 256) ||
            !IsBounded(context.SourceMetadataTrustBoundary, 256) ||
            !Sha256Pattern.IsMatch(context.SourceSha256 ?? string.Empty) ||
            !Sha256Pattern.IsMatch(context.StateSha256 ?? string.Empty) ||
            !IsBounded(context.ContextPolicyVersion, 128) ||
            context.SerializedSizeBytes is < 1 or > 32_768 ||
            context.Claim is null || !Enum.IsDefined(context.Claim.Category) ||
            !IsBounded(context.Claim.Id, 256) || !IsBounded(context.Claim.AlgorithmVersion, 256) ||
            !IsBounded(context.Claim.PermittedInterpretation, 6_144) || !IsBounded(context.Claim.UseConstraint, 512) ||
            !IsBounded(context.Claim.TextTrustBoundary, 256) ||
            context.Unknowns is null || context.Unknowns.Count > 32 || context.Unknowns.Any(item => !IsBounded(item, 512)) ||
            context.Evidence is null || context.Evidence.Count is < 1 or > MaximumEvidenceItems)
            return false;

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in context.Evidence)
        {
            if (item is null || !IsBounded(item.Id, 256) || item.Kind is not ("event" or "frame") ||
                !IsBounded(item.Relation, 128) || !IsBounded(item.TrustBoundary, 256) ||
                item.SourceCanonicalRevision <= 0 || item.SourceCanonicalRevision > context.CanonicalRevision ||
                item.Sequence < 0 || item.Sequence > context.ThroughSequence ||
                item.ClockSeconds < 0 || item.ClockSeconds > context.ProjectionAsOfTimeSeconds ||
                item.Period < 1 || Encoding.UTF8.GetByteCount(item.PayloadJson ?? string.Empty) > MaximumEvidencePayloadBytes ||
                !seenIds.Add(item.Id) || !IsJson(item.PayloadJson))
                return false;
        }

        return true;
    }

    private static bool IsVisibleEvidence(MatchIntelligenceEvidence evidence, MatchIntelligenceContextPacket context) =>
        evidence.SourceCanonicalRevision > 0 && evidence.SourceCanonicalRevision <= context.CanonicalRevision &&
        evidence.Sequence >= 0 && evidence.Sequence <= context.ThroughSequence &&
        evidence.ClockSeconds >= 0 && evidence.ClockSeconds <= context.ProjectionAsOfTimeSeconds;

    private static bool MatchesContext(CandidateExplanation candidate, MatchIntelligenceContextPacket context) =>
        string.Equals(candidate.MatchId, context.MatchId, StringComparison.Ordinal) &&
        candidate.CanonicalRevision == context.CanonicalRevision &&
        candidate.ProjectionRevision == context.ProjectionRevision &&
        candidate.SelectedTimeSeconds == context.SelectedTimeSeconds &&
        candidate.ThroughSequence == context.ThroughSequence &&
        string.Equals(candidate.SourceVersion, context.SourceVersion, StringComparison.Ordinal) &&
        string.Equals(candidate.Provenance, context.Provenance, StringComparison.Ordinal) &&
        string.Equals(candidate.SourceSha256, context.SourceSha256, StringComparison.Ordinal) &&
        string.Equals(candidate.StateSha256, context.StateSha256, StringComparison.Ordinal) &&
        string.Equals(candidate.ContextPolicyVersion, context.ContextPolicyVersion, StringComparison.Ordinal);

    private static bool IsBounded(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool IsJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var _ = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
