using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class MatchIntelligenceContextTests
{
    [Fact]
    public async Task BuildAsync_ReturnsImmutableBoundedPacketForSelectedRevisionAndTime()
    {
        var fixture = new Fixture();
        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var packet = Assert.IsType<MatchIntelligenceContextPacket>(result.Packet);
        Assert.Equal(fixture.MatchId.ToString(), packet.MatchId);
        Assert.Equal(3, packet.CanonicalRevision);
        Assert.Equal(120, packet.SelectedTimeSeconds);
        Assert.Equal(2, packet.Score.Home);
        Assert.Equal(1, packet.Score.Away);
        Assert.Equal("synthetic-source-v1", packet.SourceVersion);
        Assert.Equal("owner-confirmed-synthetic", packet.Provenance);
        Assert.Equal(MatchIntelligenceTrustBoundaries.CanonicalEvidence, packet.SourceMetadataTrustBoundary);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(packet).Length, packet.SerializedSizeBytes);
        Assert.Equal(MatchIntelligenceClaimCategory.EvidenceSupportedPattern, packet.Claim.Category);
        Assert.Contains("do not claim causation", packet.Claim.UseConstraint, StringComparison.Ordinal);
        Assert.Equal(MatchIntelligenceTrustBoundaries.CanonicalEvidence, packet.Claim.TextTrustBoundary);
        var evidence = Assert.Single(packet.Evidence);
        Assert.Equal("event-1", evidence.Id);
        Assert.Equal(MatchIntelligenceTrustBoundaries.CanonicalEvidence, evidence.TrustBoundary);
        Assert.InRange(packet.SerializedSizeBytes, 1, 32_768);
        Assert.Contains(packet.Unknowns, unknown => unknown.Contains("coach intent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_DeniesBeforeReadingWhenScopeIsNotAuthorized()
    {
        var fixture = new Fixture { Authorized = false };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.Forbidden, result.Failure);
        Assert.Equal(0, fixture.ProjectionReads);
        Assert.Equal(0, fixture.InsightReads);
    }

    [Fact]
    public async Task BuildAsync_RefusesInsightFromDifferentRevision()
    {
        var fixture = new Fixture { InsightRevision = 2 };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.ClaimUnavailable, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Fact]
    public async Task BuildAsync_RejectsUnknownTruthClass()
    {
        var fixture = new Fixture { TruthClass = "observed" };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.UnsupportedClaimCategory, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Theory]
    [InlineData("", "owner-confirmed-synthetic")]
    [InlineData("synthetic-source-v1", "")]
    public async Task BuildAsync_RefusesInsightWithoutSourceLineage(string sourceVersion, string provenance)
    {
        var fixture = new Fixture { SourceVersion = sourceVersion, Provenance = provenance };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.ClaimUnavailable, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Fact]
    public async Task BuildAsync_RejectsOversizedEvidencePayload()
    {
        var fixture = new Fixture { EvidencePayload = "{\"text\":\"" + new string('x', 300) + "\"}" };
        var limits = new MatchIntelligenceContextLimits(MaximumEvidenceItemBytes: 256);

        var result = await fixture.Builder(limits).BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.EvidenceUnavailable, result.Failure);
    }

    [Fact]
    public async Task BuildAsync_RejectsEvidenceAfterSelectedTime()
    {
        var fixture = new Fixture { EvidenceClockSeconds = 121 };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.EvidenceUnavailable, result.Failure);
    }

    [Fact]
    public async Task BuildAsync_RejectsEvidenceAfterTheInsightSequence()
    {
        var fixture = new Fixture { EvidenceSequence = 10 };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.EvidenceUnavailable, result.Failure);
    }

    [Fact]
    public async Task BuildAsync_EnforcesEvidenceLimitInUtf8Bytes()
    {
        var fixture = new Fixture { EvidencePayload = "{\"text\":\"" + new string('é', 130) + "\"}" };
        var limits = new MatchIntelligenceContextLimits(MaximumEvidenceItemBytes: 256);

        var result = await fixture.Builder(limits).BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.EvidenceUnavailable, result.Failure);
    }

    [Fact]
    public async Task BuildAsync_RefusesWhenTheFinalSerializedPacketExceedsItsLimit()
    {
        var fixture = new Fixture { EvidenceCount = 6, EvidencePayload = "{\"text\":\"" + new string('x', 5_900) + "\"}" };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.PacketTooLarge, result.Failure);
    }

    private sealed class Fixture
    {
        public MatchId MatchId { get; } = new(Guid.NewGuid());
        public bool Authorized { get; init; } = true;
        public int InsightRevision { get; init; } = 3;
        public string TruthClass { get; init; } = "evidence-supported-pattern";
        public string EvidencePayload { get; init; } = "{\"eventType\":\"pass\"}";
        public string SourceVersion { get; init; } = "synthetic-source-v1";
        public string Provenance { get; init; } = "owner-confirmed-synthetic";
        public int EvidenceClockSeconds { get; init; } = 110;
        public int ProjectionReads { get; private set; }
        public int InsightReads { get; private set; }
        public int EvidenceReads { get; private set; }

        public long EvidenceSequence { get; init; } = 9;
        public int EvidenceCount { get; init; } = 1;

        public MatchIntelligenceContextRequest Request() => new(MatchId, "insight-1", 120, 3);

        public MatchIntelligenceContextBuilder Builder(MatchIntelligenceContextLimits? limits = null) => new(
            new ProjectionReader(this), new InsightReader(this), new Authorizer(this), limits);

        private sealed class Authorizer(Fixture fixture) : IMatchIntelligenceScopeAuthorizer
        {
            public Task<bool> CanReadMatchIntelligenceAsync(MatchId matchId, CancellationToken cancellationToken) =>
                Task.FromResult(fixture.Authorized && matchId == fixture.MatchId);
        }

        private sealed class ProjectionReader(Fixture fixture) : IMatchProjectionReader
        {
            public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit, CancellationToken cancellationToken) =>
                throw new NotImplementedException();

            public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision, CancellationToken cancellationToken)
            {
                fixture.ProjectionReads++;
                var state = new MatchState(matchId.ToString(), 110, 1, MatchPhase.FirstHalf, 2, 1, null,
                    11, 11, Array.Empty<MatchPlayerState>(), 10, "synthetic-fixture-v1");
                return Task.FromResult<SelectedMatchProjection?>(new(state, 3, 10, 110, "projection-v1",
                    new string('a', 64), new string('b', 64)));
            }

            public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();

            public IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                CancellationToken cancellationToken) => throw new NotImplementedException();
        }

        private sealed class InsightReader(Fixture fixture) : IMatchInsightReader
        {
            private IReadOnlyList<PersistedEvidenceReference> References => Array.AsReadOnly(
                Enumerable.Range(1, fixture.EvidenceCount)
                    .Select(index => new PersistedEvidenceReference("event", $"event-{index}", "supports")).ToArray());

            public Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, int maximumRevision, int selectedSeconds,
                long throughSequence, long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken) =>
                throw new NotImplementedException();

            public Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, int maximumRevision,
                int selectedSeconds, long throughSequence, int maximumEvidenceItems, CancellationToken cancellationToken)
            {
                fixture.InsightReads++;
                return Task.FromResult<PersistedMatchInsight?>(new(insightId, matchId, fixture.InsightRevision, 9,
                    "press-sequence-v1", fixture.TruthClass, "A candidate pattern is supported by this event.",
                    fixture.SourceVersion, fixture.Provenance, new string('a', 64), References));
            }

            public Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
                int maximumRevision, int selectedSeconds, long throughSequence, int maximumItems, int maximumItemBytes,
                CancellationToken cancellationToken)
            {
                fixture.EvidenceReads++;
                IReadOnlyList<PersistedCanonicalEvidenceItem> rows = Array.AsReadOnly(
                    Enumerable.Range(1, fixture.EvidenceCount).Select(index =>
                        new PersistedCanonicalEvidenceItem("event", $"event-{index}", "supports", fixture.EvidenceSequence,
                            fixture.EvidenceClockSeconds, 1, fixture.EvidencePayload)).ToArray());
                return Task.FromResult<IReadOnlyList<PersistedCanonicalEvidenceItem>?>(rows);
            }
        }
    }
}
