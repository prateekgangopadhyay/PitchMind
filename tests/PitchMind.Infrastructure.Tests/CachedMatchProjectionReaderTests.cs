using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class CachedMatchProjectionReaderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CacheHitUsesExactCanonicalKeyAndDoesNotConfuseProjectionRevision()
    {
        var (matchId, projection) = CreateProjection();
        var canonicalReader = new TestProjectionReader(projection);
        var cache = new TestCache();
        var reader = new CachedMatchProjectionReader(canonicalReader, cache);

        var result = await reader.GetAtCanonicalRevisionAsync(matchId, projection.CanonicalRevision, 90,
            projection.AlgorithmVersion, CancellationToken.None);
        AssertProjection(projection, result);
        Assert.Equal(1, canonicalReader.ExactReads);
        Assert.Single(cache.Values);
        Assert.Contains(":4:90:", Assert.Single(cache.Values).Key);
        Assert.DoesNotContain(":2:90:", Assert.Single(cache.Values).Key);

        var cachedResult = await reader.GetAtCanonicalRevisionAsync(matchId, projection.CanonicalRevision, 90,
            projection.AlgorithmVersion, CancellationToken.None);
        AssertProjection(projection, cachedResult);
        Assert.Equal(1, canonicalReader.ExactReads);
    }

    [Fact]
    public async Task MissingOrCorruptCacheRebuildsFromCanonicalReader()
    {
        var (matchId, projection) = CreateProjection();
        var canonicalReader = new TestProjectionReader(projection);
        var cache = new TestCache();
        var reader = new CachedMatchProjectionReader(canonicalReader, cache);

        var first = await reader.GetAtCanonicalRevisionAsync(matchId, projection.CanonicalRevision, 90,
            projection.AlgorithmVersion, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(projection.State.HomeScore, first.State.HomeScore);
        var key = Assert.Single(cache.Values).Key;
        cache.Values[key] = Encoding.UTF8.GetBytes("{corrupt");
        var rebuilt = await reader.GetAtCanonicalRevisionAsync(matchId, projection.CanonicalRevision, 90,
            projection.AlgorithmVersion, CancellationToken.None);

        Assert.NotNull(rebuilt);
        Assert.Equal(projection.State.HomeScore, rebuilt.State.HomeScore);
        Assert.Equal(2, canonicalReader.ExactReads);
        Assert.True(JsonDocument.Parse(cache.Values[key]).RootElement.TryGetProperty("projection", out _));
    }

    [Fact]
    public async Task RedisOutageFallsBackToPostgresAndEventReadsAlwaysRemainCanonical()
    {
        var (matchId, projection) = CreateProjection();
        var canonicalReader = new TestProjectionReader(projection);
        var cache = new TestCache { ThrowOnGet = true, ThrowOnSet = true };
        var reader = new CachedMatchProjectionReader(canonicalReader, cache);

        var result = await reader.GetAtCanonicalRevisionAsync(matchId, projection.CanonicalRevision, 90,
            projection.AlgorithmVersion, CancellationToken.None);
        AssertProjection(projection, result);
        Assert.Equal(1, canonicalReader.ExactReads);

        var events = new List<MatchEvent>();
        await foreach (var item in reader.ReadEventsAtAsync(result!, 90, CancellationToken.None)) events.Add(item);
        Assert.Empty(events);
        Assert.Equal(1, canonicalReader.EventReads);
    }

    [Fact]
    public void CacheKeySeparatesMatchCanonicalRevisionSelectedTimeAndAlgorithm()
    {
        var match = MatchId.New();
        var baseline = CachedMatchProjectionReader.BuildKey(match, 4, 90, "reducer-v1");
        Assert.NotEqual(baseline, CachedMatchProjectionReader.BuildKey(MatchId.New(), 4, 90, "reducer-v1"));
        Assert.NotEqual(baseline, CachedMatchProjectionReader.BuildKey(match, 5, 90, "reducer-v1"));
        Assert.NotEqual(baseline, CachedMatchProjectionReader.BuildKey(match, 4, 91, "reducer-v1"));
        Assert.NotEqual(baseline, CachedMatchProjectionReader.BuildKey(match, 4, 90, "reducer-v2"));
    }

    [Fact]
    public async Task FlushedCacheRebuildsWithoutAlteringCanonicalScoreOrEvidence()
    {
        var (matchId, projection) = CreateProjection();
        var canonicalReader = new TestProjectionReader(projection);
        var memoryCache = new MemoryMatchProjectionCache();
        var reader = new CachedMatchProjectionReader(canonicalReader, memoryCache);

        var initial = await reader.GetAtAsync(matchId, 90, projection.CanonicalRevision, projection.AlgorithmVersion, CancellationToken.None);
        AssertProjection(projection, initial);
        Assert.Equal(1, canonicalReader.ExactReads);

        // Simulate complete Redis flush / restart
        memoryCache.Clear();

        var rebuilt = await reader.GetAtAsync(matchId, 90, projection.CanonicalRevision, projection.AlgorithmVersion, CancellationToken.None);
        AssertProjection(projection, rebuilt);
        Assert.Equal(2, canonicalReader.ExactReads);
        Assert.Equal(projection.State.HomeScore, rebuilt!.State.HomeScore);
        Assert.Equal(projection.State.AwayScore, rebuilt.State.AwayScore);
        Assert.Equal(projection.StateSha256, rebuilt.StateSha256);
    }

    private static (MatchId MatchId, SelectedMatchProjection Projection) CreateProjection()
    {
        var matchId = MatchId.New();
        var state = new MatchState(matchId.ToString(), 80, 1, MatchPhase.FirstHalf, 1, 0, "home",
            11, 11, Array.Empty<MatchPlayerState>(), 3, "synthetic-test");
        var stateJson = JsonSerializer.Serialize(state, JsonOptions);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        return (matchId, new SelectedMatchProjection(state, 4, 2, 3, 80, "reducer-v1",
            new string('a', 64), hash));
    }

    private static void AssertProjection(SelectedMatchProjection expected, SelectedMatchProjection? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.CanonicalRevision, actual.CanonicalRevision);
        Assert.Equal(expected.ProjectionRevision, actual.ProjectionRevision);
        Assert.Equal(expected.StateSha256, actual.StateSha256);
        Assert.Equal(expected.State.HomeScore, actual.State.HomeScore);
        Assert.Equal(expected.State.MatchId, actual.State.MatchId);
    }

    private sealed class TestCache : IMatchProjectionCache
    {
        public Dictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);
        public bool ThrowOnGet { get; init; }
        public bool ThrowOnSet { get; init; }

        public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
        {
            if (ThrowOnGet) throw new IOException("Simulated Redis outage.");
            return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken)
        {
            if (ThrowOnSet) throw new IOException("Simulated Redis outage.");
            Assert.True(timeToLive > TimeSpan.Zero);
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TestProjectionReader(SelectedMatchProjection projection) : IMatchProjectionReader
    {
        public int ExactReads { get; private set; }
        public int EventReads { get; private set; }

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
            long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
            Task.FromResult<SelectedMatchProjection?>(projection);

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
            long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
        {
            ExactReads++;
            return Task.FromResult<SelectedMatchProjection?>(projection);
        }

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection selectedProjection,
            int selectedSeconds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            EventReads++;
            await Task.CompletedTask;
            yield break;
        }

        public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection, int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
