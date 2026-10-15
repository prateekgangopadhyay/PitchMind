namespace PitchMind.Application.MatchIntelligence;

public enum CandidateExplanationSourceTextFailure
{
    None,
    LineageRejected,
    UnsupportedCandidateText,
    IncompleteSourceEvidence
}

/// <summary>
/// A narrow, non-publishing check that permits only a verbatim restatement of the selected
/// canonical insight with its complete evidence set. It does not prove the insight itself or
/// arbitrary natural-language claims semantically correct.
/// </summary>
public sealed record CandidateExplanationSourceTextCheck(
    bool IsSupportedBySelectedInsight,
    CandidateExplanationSourceTextFailure Failure,
    MatchIntelligenceClaimCategory? SourceTruthClass,
    IReadOnlyList<string> SourceEvidenceIds,
    CandidateExplanationLineageFailure? LineageFailure)
{
    /// <summary>This constrained source-text check never authorizes publication.</summary>
    public bool CanBePublished => false;
}

/// <summary>
/// Rejects model-added claims by allowing only the exact selected insight text and all evidence
/// references already bound into its packet. This is not the final EP08-S04 semantic verifier.
/// </summary>
public sealed class CandidateExplanationSourceTextGuard(CandidateExplanationLineageChecker? lineageChecker = null)
{
    private readonly CandidateExplanationLineageChecker _lineageChecker = lineageChecker ?? new();

    public CandidateExplanationSourceTextCheck Check(
        CandidateExplanation? candidate,
        MatchIntelligenceContextPacket? context)
    {
        var lineage = _lineageChecker.Check(candidate, context);
        if (!lineage.IsLineageBound)
            return new(false, CandidateExplanationSourceTextFailure.LineageRejected, null,
                Array.Empty<string>(), lineage.Failure);

        var sourceEvidenceIds = Array.AsReadOnly(context!.Evidence.Select(item => item.Id).ToArray());
        if (candidate!.Claims.Count != 1 ||
            !string.Equals(candidate.Text, context.Claim.PermittedInterpretation, StringComparison.Ordinal) ||
            !string.Equals(candidate.Claims[0].Text, context.Claim.PermittedInterpretation, StringComparison.Ordinal))
            return new(false, CandidateExplanationSourceTextFailure.UnsupportedCandidateText,
                context.Claim.Category, sourceEvidenceIds, null);

        var citedEvidenceIds = candidate.Claims[0].EvidenceIds.ToHashSet(StringComparer.Ordinal);
        if (!citedEvidenceIds.SetEquals(sourceEvidenceIds))
            return new(false, CandidateExplanationSourceTextFailure.IncompleteSourceEvidence,
                context.Claim.Category, sourceEvidenceIds, null);

        return new(true, CandidateExplanationSourceTextFailure.None, context.Claim.Category, sourceEvidenceIds, null);
    }
}
