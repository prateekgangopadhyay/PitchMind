using PitchMind.Application.Abstractions;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class EvidenceExportServiceTests
{
    private readonly EvidenceExportService _service = new();

    private static SelectedMatchProjection CreateProjection(string matchId = "match-test-export-01")
    {
        var state = new MatchState(
            MatchId: matchId,
            ClockSeconds: 3600,
            Period: 2,
            Phase: MatchPhase.SecondHalf,
            HomeScore: 1,
            AwayScore: 0,
            PossessionTeamId: "home",
            HomeActivePlayers: 11,
            AwayActivePlayers: 11,
            Players: [],
            LastSequence: 200,
            Provenance: "Simulated match feed");

        return new SelectedMatchProjection(
            State: state,
            CanonicalRevision: 10,
            ProjectionRevision: 2,
            AsOfSequence: 200,
            AsOfClockSeconds: 3600,
            AlgorithmVersion: "test-algo-v1",
            SourceSha256: new string('c', 64),
            StateSha256: new string('d', 64));
    }

    [Theory]
    [InlineData("analyst")]
    [InlineData("broadcaster")]
    [InlineData("admin")]
    public void CreateExport_Succeeds_For_Permitted_Roles(string role)
    {
        var projection = CreateProjection();
        var evidence = new List<ExportEvidenceItem>
        {
            new("ev-1", "press-breakthrough", "Observation", "High pressing shape verified", 3600, "src-hash")
        };

        var export = _service.CreateExport(projection, evidence, "user-auditor-1", role, 3600);

        Assert.NotNull(export);
        Assert.Equal("match-test-export-01", export.MatchId);
        Assert.Equal(10L, export.CanonicalRevision);
        Assert.Equal(3600, export.SelectedTimeSeconds);
        Assert.Equal(1, export.EvidenceItemCount);
        Assert.NotEmpty(export.Signature);
        Assert.True(export.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var isValid = _service.ValidateExport(export, out var error);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("fan")]
    [InlineData("viewer")]
    [InlineData("")]
    [InlineData(null)]
    public void CreateExport_Throws_UnauthorizedAccessException_For_Non_Permitted_Roles(string? role)
    {
        var projection = CreateProjection();

        Assert.Throws<UnauthorizedAccessException>(() =>
            _service.CreateExport(projection, [], "user-123", role!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateExport_Throws_ArgumentException_When_UserId_Missing(string? userId)
    {
        var projection = CreateProjection();

        Assert.Throws<ArgumentException>(() =>
            _service.CreateExport(projection, [], userId!, "analyst"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(700000)] // Exceeds 7 days max TTL
    public void CreateExport_Throws_ArgumentOutOfRangeException_When_TTL_Invalid(int expirationSeconds)
    {
        var projection = CreateProjection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CreateExport(projection, [], "user-1", "analyst", expirationSeconds));
    }

    [Fact]
    public void ValidateExport_Fails_When_Artifact_Is_Expired()
    {
        var projection = CreateProjection();
        var export = _service.CreateExport(projection, [], "user-1", "analyst", 60);

        // Fabricate an expired package
        var expiredExport = export with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };

        var isValid = _service.ValidateExport(expiredExport, out var error);

        Assert.False(isValid);
        Assert.Contains("EXPIRED_EXPORT_ARTIFACT", error);
    }

    [Fact]
    public void ValidateExport_Fails_When_Signature_Or_Payload_Tampered()
    {
        var projection = CreateProjection();
        var export = _service.CreateExport(projection, [], "user-1", "analyst", 3600);

        // Tamper canonical revision
        var tamperedExport = export with { CanonicalRevision = 999 };

        var isValid = _service.ValidateExport(tamperedExport, out var error);

        Assert.False(isValid);
        Assert.Contains("INVALID_SIGNATURE", error);
    }
}
