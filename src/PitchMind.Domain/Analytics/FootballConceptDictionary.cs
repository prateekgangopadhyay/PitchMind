using System.Text.Json;

namespace PitchMind.Domain.Analytics;

internal sealed record FootballConceptDictionary(
    string SchemaVersion,
    string DictionaryId,
    string PolicyVersion,
    string ReviewStatus,
    FootballSourceProfileStatus SourceProfile,
    FootballConceptApprovals Approvals,
    IReadOnlyList<FootballConceptDefinition> Concepts)
{
    private static readonly string[] RequiredConceptIds =
        ["press", "trap", "response", "danger", "overload", "line-break", "counterpress", "rest-defence"];

    public static FootballConceptDictionary Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dictionary = JsonSerializer.Deserialize<FootballConceptDictionary>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Football concept dictionary is empty.");
        dictionary.Validate();
        return dictionary;
    }

    private void Validate()
    {
        if (ReviewStatus != "proposed")
            throw new InvalidDataException("Concept proposal cannot be marked approved until a trusted approval verifier is implemented.");

        if (SchemaVersion != "1.0.0" || string.IsNullOrWhiteSpace(DictionaryId) ||
            string.IsNullOrWhiteSpace(PolicyVersion) ||
            SourceProfile is null || Approvals is null || Concepts is null)
            throw new InvalidDataException("Football concept dictionary metadata is invalid.");

        if (string.IsNullOrWhiteSpace(SourceProfile.CandidateManifest) ||
            !System.Text.RegularExpressions.Regex.IsMatch(SourceProfile.CandidateSourceSha256 ?? "", "^[a-f0-9]{64}$") ||
            SourceProfile.CanonicalMappingStatus != "not-approved" ||
            SourceProfile.CoordinateSemanticsStatus != "unverified" ||
            SourceProfile.PlayerMetricStatus != "unvalidated" ||
            SourceProfile.FootballReviewStatus != "required")
            throw new InvalidDataException("Football concept proposal must retain unresolved source-review states.");

        if (Approvals.ProductOwner is not null || Approvals.FootballAnalyst is not null || Approvals.QaReviewer is not null)
            throw new InvalidDataException("Football concept proposal cannot contain approval references without a trusted approval verifier.");

        if (Concepts.Any(concept => concept is null || string.IsNullOrWhiteSpace(concept.Id)))
            throw new InvalidDataException("Football concepts require stable IDs.");
        if (Concepts.Select(concept => concept.Id).Distinct(StringComparer.Ordinal).Count() != Concepts.Count)
            throw new InvalidDataException("Football concept IDs must be unique.");
        var byId = Concepts.ToDictionary(concept => concept.Id, StringComparer.Ordinal);
        if (RequiredConceptIds.Any(id => !byId.ContainsKey(id)) || Concepts.Count != RequiredConceptIds.Length)
            throw new InvalidDataException("Football concept dictionary must define the complete required taxonomy exactly once.");

        foreach (var concept in Concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Term) || string.IsNullOrWhiteSpace(concept.Definition) ||
                string.IsNullOrWhiteSpace(concept.NonDefinition) || string.IsNullOrWhiteSpace(concept.ThresholdStatus) ||
                concept.Phases is null || concept.Phases.Count == 0 || concept.Phases.Any(string.IsNullOrWhiteSpace) ||
                concept.MinimumEvidence is null || concept.MinimumEvidence.Count == 0 ||
                concept.InsufficientEvidence is null || concept.InsufficientEvidence.Count == 0 ||
                concept.ProhibitedClaims is null || concept.ProhibitedClaims.Count == 0 ||
                concept.MinimumEvidence.Any(string.IsNullOrWhiteSpace) ||
                concept.InsufficientEvidence.Any(string.IsNullOrWhiteSpace) ||
                concept.ProhibitedClaims.Any(string.IsNullOrWhiteSpace) ||
                !concept.ThresholdStatus.StartsWith("unapproved;", StringComparison.Ordinal))
                throw new InvalidDataException($"Football concept '{concept.Id}' requires definition, non-definition, phases, evidence minima, insufficiency rules, prohibited claims, and threshold status.");
        }
    }
}

internal sealed record FootballSourceProfileStatus(
    string CandidateManifest,
    string CandidateSourceSha256,
    string CanonicalMappingStatus,
    string CoordinateSemanticsStatus,
    string PlayerMetricStatus,
    string FootballReviewStatus);

internal sealed record FootballConceptApprovals(string? ProductOwner, string? FootballAnalyst, string? QaReviewer);

internal sealed record FootballConceptDefinition(
    string Id,
    string Term,
    string Definition,
    string NonDefinition,
    IReadOnlyList<string> Phases,
    IReadOnlyList<string> MinimumEvidence,
    IReadOnlyList<string> InsufficientEvidence,
    IReadOnlyList<string> ProhibitedClaims,
    string ThresholdStatus);
