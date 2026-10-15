using PitchMind.Application.Abstractions;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class AutoRecapGeneratorTests
{
    [Fact]
    public void GenerateDeterministicRecap_Creates_Accurate_Recap_From_Canonical_State()
    {
        var generator = new AutoRecapGenerator();

        var state = new MatchState(
            MatchId: "match-recap-001",
            ClockSeconds: 5400,
            Period: 2,
            Phase: MatchPhase.SecondHalf,
            HomeScore: 2,
            AwayScore: 1,
            PossessionTeamId: "home",
            HomeActivePlayers: 11,
            AwayActivePlayers: 10,
            Players: [],
            LastSequence: 100,
            Provenance: "Simulated EPL-context match");

        var projection = new SelectedMatchProjection(
            State: state,
            CanonicalRevision: 42,
            ProjectionRevision: 5,
            AsOfSequence: 100,
            AsOfClockSeconds: 5400,
            AlgorithmVersion: "recap-rules-v1",
            SourceSha256: new string('a', 64),
            StateSha256: new string('b', 64));

        var keyMoments = new List<AutoRecapInsight>
        {
            new("insight-1", "match-recap-001", 1200, 1, 20, "Early home breakthrough after sustained pressing.", "press-high", "High"),
            new("insight-2", "match-recap-001", 4500, 2, 75, "Late red card for away defender alters tactical shape.", "red-card", "High")
        };

        var response = generator.GenerateDeterministicRecap(projection, keyMoments, "Manchester United vs Liverpool");

        Assert.Equal("match-recap-001", response.MatchId);
        Assert.Equal(42L, response.CanonicalRevision);
        Assert.Contains("Manchester United vs Liverpool", response.Headline);
        Assert.Contains("2-1", response.Headline);
        Assert.Contains("Home Victory", response.Headline);
        Assert.Equal(2, response.KeyMomentsSummary.Count);
        Assert.Contains("1200s", response.KeyMomentsSummary[0]);
        Assert.Contains("4500s", response.KeyMomentsSummary[1]);
        Assert.False(response.GeneratedByAi);
        Assert.Equal("Simulated EPL-context match", response.Provenance);
    }
}
