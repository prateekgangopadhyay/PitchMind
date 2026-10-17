using PitchMind.Application.MatchIntelligence;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class HistoricalAndSpatialServiceTests
{
    [Fact]
    public async Task SearchAsync_FindsEvidenceByKeyword()
    {
        var service = new HistoricalEvidenceSearchService();
        var query = new HistoricalSearchQuery(
            MatchId: null,
            SearchText: "Zone 14",
            TruthClass: null,
            MinimumConfidence: null,
            AlgorithmVersion: null);

        var result = await service.SearchAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
        Assert.All(result.Results, r => Assert.Contains("Zone 14", r.Summary + r.Title));
    }

    [Fact]
    public async Task SearchAsync_FiltersByTruthClass()
    {
        var service = new HistoricalEvidenceSearchService();
        var query = new HistoricalSearchQuery(
            MatchId: null,
            SearchText: null,
            TruthClass: "ObservedFact",
            MinimumConfidence: null,
            AlgorithmVersion: null);

        var result = await service.SearchAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.All(result.Results, r => Assert.Equal("ObservedFact", r.TruthClass));
    }

    [Fact]
    public void GetSpatialSummary_ComputesCorridorsAndThirds()
    {
        var service = new SpatialAnalysisService();
        var summary = service.GetSpatialSummary("match-test-1", 4044);

        Assert.NotNull(summary);
        Assert.Equal("match-test-1", summary.MatchId);
        Assert.Equal(5, summary.Corridors.Count);
        Assert.Contains(summary.Corridors, c => c.ChannelNumber == 2 && c.CorridorName == "Left Half-Space");
        Assert.True(summary.PitchThirds.AttackingThirdPct > 0);
        Assert.True(summary.Zone14OccupancyPct > 0);
    }

    [Fact]
    public void GetCapabilities_ReturnsActivePlatformProviders()
    {
        var service = new PartnerIntegrationService();
        var caps = service.GetCapabilities();

        Assert.NotNull(caps);
        Assert.NotEmpty(caps.Providers);
        Assert.Contains(caps.Providers, p => p.ProviderId == "azure-managed-redis");
        Assert.Contains(caps.Providers, p => p.ProviderId == "azure-foundry-gpt4o");
    }
}
