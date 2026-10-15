using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
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
        Assert.Equal(5, packet.CanonicalRevision);
        Assert.Equal(3, packet.ProjectionRevision);
        Assert.Equal(120, packet.SelectedTimeSeconds);
        Assert.Equal(2, packet.Score.Home);
        Assert.Equal(1, packet.Score.Away);
        Assert.Equal("synthetic-source-v1", packet.SourceVersion);
        Assert.Equal("owner-confirmed-synthetic", packet.Provenance);
        Assert.Contains("untrusted", packet.SourceMetadataTrustBoundary, StringComparison.Ordinal);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(packet).Length, packet.SerializedSizeBytes);
        Assert.Equal(MatchIntelligenceClaimCategory.EvidenceSupportedPattern, packet.Claim.Category);
        Assert.Contains("do not claim causation", packet.Claim.UseConstraint, StringComparison.Ordinal);
        Assert.Contains("untrusted", packet.Claim.TextTrustBoundary, StringComparison.Ordinal);
        var evidence = Assert.Single(packet.Evidence);
        Assert.Equal("event-1", evidence.Id);
        Assert.Contains("untrusted", evidence.TrustBoundary);
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
    public async Task BuildAsync_RefusesInsightFromDifferentCanonicalRevision()
    {
        var fixture = new Fixture { InsightCanonicalRevision = 4 };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.ClaimUnavailable, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Fact]
    public async Task BuildAsync_RejectsUnknownTruthClass()
    {
        var fixture = new Fixture { TruthClass = "unclassified" };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.UnsupportedClaimCategory, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Theory]
    [InlineData("observed", MatchIntelligenceClaimCategory.ObservedFact)]
    [InlineData("derived", MatchIntelligenceClaimCategory.DerivedFact)]
    [InlineData("evidence-supported-pattern", MatchIntelligenceClaimCategory.EvidenceSupportedPattern)]
    [InlineData("supported-interpretation", MatchIntelligenceClaimCategory.SupportedInterpretation)]
    [InlineData("hypothesis", MatchIntelligenceClaimCategory.Hypothesis)]
    [InlineData("simulated", MatchIntelligenceClaimCategory.Simulated)]
    public async Task BuildAsync_PreservesPersistedTruthClass(string truthClass, MatchIntelligenceClaimCategory expectedCategory)
    {
        var fixture = new Fixture { TruthClass = truthClass };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedCategory, result.Packet!.Claim.Category);
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
    public async Task BuildAsync_RejectsInsightFromDifferentProjectionRevision()
    {
        var fixture = new Fixture { InsightProjectionRevision = 2 };

        var result = await fixture.Builder().BuildAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(MatchIntelligenceContextFailure.ClaimUnavailable, result.Failure);
        Assert.Equal(0, fixture.EvidenceReads);
    }

    [Fact]
    public async Task BuildAsync_RejectsInsightWhoseSourceHashDoesNotMatchSelectedProjection()
    {
        var fixture = new Fixture { InsightSourceSha256 = new string('c', 64) };

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
        public int InsightProjectionRevision { get; init; } = 3;
        public long InsightCanonicalRevision { get; init; } = 5;
        public string InsightSourceSha256 { get; init; } = new string('a', 64);
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

        public MatchIntelligenceContextRequest Request() => new(MatchId, "insight-1", 120, 5, 3);

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

            public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
                long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken)
            {
                fixture.ProjectionReads++;
                var state = new MatchState(matchId.ToString(), 110, 1, MatchPhase.FirstHalf, 2, 1, null,
                    11, 11, Array.Empty<MatchPlayerState>(), 10, "synthetic-fixture-v1");
                return Task.FromResult<SelectedMatchProjection?>(new(state, 5, 3, 10, 110, "projection-v1",
                    new string('a', 64), new string('b', 64)));
            }

            public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();

            public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
                long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) => throw new NotImplementedException();

            public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
                long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) => throw new NotImplementedException();

            public IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                CancellationToken cancellationToken) => throw new NotImplementedException();

            public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) => throw new NotImplementedException();

            public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) => throw new NotImplementedException();

            public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                CancellationToken cancellationToken) => throw new NotImplementedException();

            public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection, int selectedSeconds,
                CancellationToken cancellationToken) => throw new NotImplementedException();
        }

        private sealed class InsightReader(Fixture fixture) : IMatchInsightReader
        {
            private IReadOnlyList<PersistedEvidenceReference> References => Array.AsReadOnly(
                Enumerable.Range(1, fixture.EvidenceCount)
                    .Select(index => new PersistedEvidenceReference("event", $"event-{index}", "supports")).ToArray());

            public Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, long maximumCanonicalRevision,
                int maximumProjectionRevision, int selectedSeconds,
                long throughSequence, long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken) =>
                throw new NotImplementedException();

            public Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, long maximumCanonicalRevision,
                int maximumProjectionRevision, int selectedSeconds, long throughSequence, CancellationToken cancellationToken,
                int maximumEvidenceItems = 100)
            {
                fixture.InsightReads++;
                return Task.FromResult<PersistedMatchInsight?>(new(insightId, matchId, fixture.InsightCanonicalRevision,
                    fixture.InsightProjectionRevision, 9,
                    "press-sequence-v1", fixture.TruthClass, "A candidate pattern is supported by this event.",
                    fixture.SourceVersion, fixture.Provenance, fixture.InsightSourceSha256, References));
            }

            public Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
                long maximumCanonicalRevision, int maximumProjectionRevision, int selectedSeconds, long throughSequence,
                CancellationToken cancellationToken, int maximumItems = 100, int maximumItemBytes = 65_536)
            {
                fixture.EvidenceReads++;
                IReadOnlyList<PersistedCanonicalEvidenceItem> rows = Array.AsReadOnly(
                    Enumerable.Range(1, fixture.EvidenceCount).Select(index =>
                        new PersistedCanonicalEvidenceItem("event", $"event-{index}", "supports", 5, fixture.EvidenceSequence,
                            fixture.EvidenceClockSeconds, 1, fixture.EvidencePayload)).ToArray());
                return Task.FromResult<IReadOnlyList<PersistedCanonicalEvidenceItem>?>(rows);
            }
        }
    }
}
