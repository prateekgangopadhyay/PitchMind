using PitchMind.Domain.Match;

namespace PitchMind.Application.MatchIntelligence;

public sealed record HistoricalSearchQuery(
    string? MatchId,
    string? SearchText,
    string? TruthClass,
    double? MinimumConfidence,
    string? AlgorithmVersion,
    int Limit = 20);

public sealed record HistoricalSearchResultItem(
    string InsightId,
    string MatchId,
    int ClockSeconds,
    int MatchPeriod,
    long SequenceNumber,
    string Title,
    string Summary,
    string TruthClass,
    double Confidence,
    string AlgorithmVersion,
    string Provenance,
    IReadOnlyList<string> Citations);

public sealed record HistoricalSearchResponse(
    IReadOnlyList<HistoricalSearchResultItem> Results,
    int TotalCount,
    string FilterSummary,
    DateTimeOffset SearchedAtUtc);

public interface IHistoricalEvidenceSearchService
{
    Task<HistoricalSearchResponse> SearchAsync(HistoricalSearchQuery query, CancellationToken cancellationToken);
}

public sealed class HistoricalEvidenceSearchService : IHistoricalEvidenceSearchService
{
    // Golden indexed dataset of verified tactical events and observations
    private static readonly IReadOnlyList<HistoricalSearchResultItem> HistoricalRepository = BuildRepository();

    private static List<HistoricalSearchResultItem> BuildRepository() =>
    [
        new(
            "m1-press-breakthrough-01",
            "e0000000-0000-0000-0000-000000000001",
            1000,
            1,
            10001L,
            "High Press Turnover & Goal Exploitation",
            "High press triggered in Zone 14 forced turnover leading directly to assist and goal.",
            "ObservedFact",
            0.98,
            "press-detector-v1",
            "Canonical EPL-simulation",
            ["ev-goal-seq-1", "pass-assist", "press-z14-turnover"]),

        new(
            "m1-left-halfspace-overload-02",
            "e0000000-0000-0000-0000-000000000001",
            2000,
            1,
            10002L,
            "Left Half-Space Overload Corridor",
            "Overload combination exploited Channel 2 (LHS) pinning the opposing full-back.",
            "DeterministicDerivation",
            0.94,
            "corridor-analysis-v1",
            "Canonical EPL-simulation",
            ["pass-linebreaking", "pass-combination"]),

        new(
            "m1-counter-press-zone14-03",
            "e0000000-0000-0000-0000-000000000001",
            3000,
            2,
            10003L,
            "Zone 14 Edge-of-Box Containment",
            "Defensive recovery prevented shooting opportunity from central edge of 18-yard box.",
            "SupportedInterpretation",
            0.88,
            "zone-containment-v1",
            "Canonical EPL-simulation",
            ["def-block", "zone14-intercept"])
    ];

    public Task<HistoricalSearchResponse> SearchAsync(HistoricalSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = HistoricalRepository.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.MatchId))
        {
            filtered = filtered.Where(x => string.Equals(x.MatchId, query.MatchId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.TruthClass))
        {
            filtered = filtered.Where(x => string.Equals(x.TruthClass, query.TruthClass, StringComparison.OrdinalIgnoreCase));
        }

        if (query.MinimumConfidence.HasValue)
        {
            filtered = filtered.Where(x => x.Confidence >= query.MinimumConfidence.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.AlgorithmVersion))
        {
            filtered = filtered.Where(x => string.Equals(x.AlgorithmVersion, query.AlgorithmVersion, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            filtered = filtered.Where(x =>
                x.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                x.Summary.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                x.Citations.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase)));
        }

        var results = filtered.Take(Math.Max(1, Math.Min(100, query.Limit))).ToList();

        var filterSummary = $"Query: '{query.SearchText ?? "ALL"}', TruthClass: '{query.TruthClass ?? "ANY"}', MinConf: {query.MinimumConfidence?.ToString("F2") ?? "N/A"}";

        return Task.FromResult(new HistoricalSearchResponse(
            Results: results,
            TotalCount: results.Count,
            FilterSummary: filterSummary,
            SearchedAtUtc: DateTimeOffset.UtcNow));
    }
}
