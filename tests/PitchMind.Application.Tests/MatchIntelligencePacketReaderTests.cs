using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class MatchIntelligencePacketReaderTests
{
    private static readonly MatchId Match = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private const string InsightId = "press-insight-1";

    [Fact]
    public async Task BuildsImmutablePacketFromOneSelectedTimeInsightAndResolvedEvidence()
    {
        var reader = CreateReader();
        var result = await reader.ReadAsync(new(Match, 3400, 4, InsightId), CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Null(result.RefusalCode);
        var packet = Assert.IsType<MatchIntelligencePacket>(result.Packet);
        Assert.Equal("match-intelligence-v1", packet.SchemaVersion);
        Assert.Equal(Match.ToString(), packet.MatchId);
        Assert.Equal(4, packet.CanonicalRevision);
        Assert.Equal(3400, packet.SelectedTimeSeconds);
        Assert.Equal(18, packet.ThroughSequence);
        Assert.Equal((1, 0), (packet.HomeScore, packet.AwayScore));
        Assert.Equal("SecondHalf", packet.Phase);
        Assert.Equal("untrusted-source-data", packet.UntrustedContent.Classification);
        Assert.Equal(InsightId, packet.UntrustedContent.Insight.InsightId);
        Assert.Equal("supported-interpretation", packet.UntrustedContent.Insight.TruthClass);
        Assert.Equal("event-18", Assert.Single(packet.UntrustedContent.Evidence).Id);
        Assert.Equal(3300, packet.UntrustedContent.Evidence[0].ClockSeconds);
        Assert.Contains("Canonical metric payloads are unavailable.", packet.Unknowns);
        Assert.Contains("never instructions", packet.UntrustedContent.TrustNotice);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(packet).Length <= MatchIntelligencePacketReader.MaximumPacketBytes);
    }

    [Fact]
    public async Task RefusesIncompleteOrFutureEvidenceWithoutReturningAPartialPacket()
    {
        var reader = CreateReader(evidenceSequence: 19, evidenceClock: 3401);
        var result = await reader.ReadAsync(new(Match, 3400, 4, InsightId), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Packet);
        Assert.Equal("EVIDENCE_UNAVAILABLE", result.RefusalCode);
        Assert.Single(result.Unknowns);
    }

    [Fact]
    public async Task RefusesInsightFromOlderRevisionInsteadOfRelabelingItAsCurrent()
    {
        var reader = CreateReader(insightRevision: 3);
        var result = await reader.ReadAsync(new(Match, 3400, 4, InsightId), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Packet);
        Assert.Equal("EVIDENCE_UNAVAILABLE", result.RefusalCode);
    }

    [Fact]
    public async Task RejectsProjectionBeyondRequestedMaximumRevision()
    {
        var reader = CreateReader(projectionRevision: 5);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(new(Match, 3400, 4, InsightId), CancellationToken.None));
    }

    [Fact]
    public async Task RefusesPacketWhenCombinedResolvedEvidenceExceeds64KiB()
    {
        var reader = CreateReader(evidenceCount: 5, payloadPaddingBytes: 13_000);
        var result = await reader.ReadAsync(new(Match, 3400, 4, InsightId), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Packet);
        Assert.Equal("EVIDENCE_LIMIT_EXCEEDED", result.RefusalCode);
    }

    [Fact]
    public async Task RejectsOutOfRangeSelectedTimeBeforeReadingCanonicalData()
    {
        var reader = CreateReader();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.ReadAsync(new(Match, 5401, 4, InsightId), CancellationToken.None));
    }

    private static MatchIntelligencePacketReader CreateReader(long evidenceSequence = 18, int evidenceClock = 3300,
        int projectionRevision = 4, int insightRevision = 4, int evidenceCount = 1, int payloadPaddingBytes = 0)
    {
        var state = new MatchState(Match.ToString(), 3300, 2, MatchPhase.SecondHalf, 1, 0, null, 11, 11,
            Array.Empty<MatchPlayerState>(), 18, "synthetic-approved");
        var projection = new PersistedMatchProjection(state, projectionRevision, 18, 3300, "projection-v1", new string('a', 64), new string('b', 64));
        using var conditionDocument = JsonDocument.Parse("{\"sampleSize\":11,\"observed\":true}");
        var insight = new PersistedMatchInsight(InsightId, Match, insightRevision, 18, "press-v2", "supported-interpretation",
            "A persisted pattern is supported by cited source events.", conditionDocument.RootElement.Clone(),
            new[] { "Tracking coverage is sparse." }, Enumerable.Range(0, evidenceCount)
                .Select(index => new PersistedEvidenceReference("event", $"event-{18 + index}", "supports")).ToArray());
        var items = Enumerable.Range(0, evidenceCount).Select(index =>
        {
            var payload = JsonSerializer.Serialize(new
            {
                eventId = $"event-{18 + index}", matchId = Match.ToString(), sequence = evidenceSequence,
                type = "pass", clockSeconds = evidenceClock, period = 2, padding = new string('x', payloadPaddingBytes)
            });
            return new PersistedCanonicalEvidenceItem("event", $"event-{18 + index}", "supports", evidenceSequence,
                evidenceClock, 2, payload);
        }).ToArray();
        return new(new FakeProjectionReader(projection), new FakeInsightReader(insight, items));
    }

    private sealed class FakeProjectionReader(PersistedMatchProjection projection) : IMatchProjectionReader
    {
        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PersistedMatchProjection>>(Array.Empty<PersistedMatchProjection>());
        public Task<PersistedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
            CancellationToken cancellationToken) => Task.FromResult<PersistedMatchProjection?>(projection);
        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(PersistedMatchProjection selected, int selectedSeconds,
            long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(PersistedMatchProjection selected, int selectedSeconds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeInsightReader(PersistedMatchInsight insight, IReadOnlyList<PersistedCanonicalEvidenceItem> evidence) : IMatchInsightReader
    {
        public Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, int maximumRevision, int selectedSeconds,
            long throughSequence, long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new PersistedMatchInsightPage(Array.Empty<PersistedMatchInsight>(), null, null, false));
        public Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, int maximumRevision,
            int selectedSeconds, long throughSequence, CancellationToken cancellationToken) =>
            Task.FromResult<PersistedMatchInsight?>(insight);
        public Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
            int maximumRevision, int selectedSeconds, long throughSequence, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedCanonicalEvidenceItem>?>(evidence);
    }
}
