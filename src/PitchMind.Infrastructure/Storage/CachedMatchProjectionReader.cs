using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Cache-aside wrapper for exact-revision match projections. A Redis adapter implements IMatchProjectionCache;
/// errors, timeouts and invalid payloads always degrade safely to the canonical PostgreSQL reader.
/// </summary>
public sealed class CachedMatchProjectionReader(
    IMatchProjectionReader canonicalReader,
    IMatchProjectionCache cache,
    TimeSpan? timeToLive = null) : IMatchProjectionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);
    private const int MaximumCacheEntryBytes = 1_048_576;

    public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
        CancellationToken cancellationToken) => canonicalReader.ListLatestAsync(afterMatchId, limit, cancellationToken);

    public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
        long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
        canonicalReader.GetForApiAtAsync(matchId, selectedSeconds, maximumCanonicalRevision, maximumProjectionRevision, cancellationToken);

    public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        long afterSequence, int limit, CancellationToken cancellationToken) =>
        canonicalReader.ReadEventsPageAtAsync(projection, selectedSeconds, afterSequence, limit, cancellationToken);

    public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
        canonicalReader.ReadReplaySnapshotAtAsync(matchId, selectedSeconds, upToCanonicalRevision, algorithmVersion, cancellationToken);

    public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
        GetAtCanonicalRevisionAsync(matchId, upToCanonicalRevision, selectedSeconds, algorithmVersion, cancellationToken);

    public async Task<SelectedMatchProjection?> GetAtCanonicalRevisionAsync(MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion, CancellationToken cancellationToken)
    {
        if (timeToLive.HasValue && timeToLive.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        var key = BuildKey(matchId, canonicalRevision, selectedSeconds, algorithmVersion);
        try
        {
            var value = await cache.GetAsync(key, cancellationToken);
            if (value is { Length: > 0 and <= MaximumCacheEntryBytes })
            {
                var cached = TryRead(value, matchId, canonicalRevision, selectedSeconds, algorithmVersion);
                if (cached is not null) return cached;
                await TryRemoveAsync(key, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Cache transport and deserialization errors degrade cleanly to canonical store.
        }

        var projection = await canonicalReader.GetAtCanonicalRevisionAsync(
            matchId, canonicalRevision, selectedSeconds, algorithmVersion, cancellationToken);
        if (projection is null) return null;
        if (!Matches(projection, matchId, canonicalRevision, selectedSeconds, algorithmVersion))
            throw new InvalidDataException("Canonical reader returned a projection outside the requested cache identity.");

        var bytes = SerializeCacheValue(projection, selectedSeconds);
        if (bytes.Length <= MaximumCacheEntryBytes)
        {
            try
            {
                await cache.SetAsync(key, bytes, timeToLive ?? DefaultTimeToLive, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A failed cache write must never fail a successful canonical read.
            }
        }
        return projection;
    }

    public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Event data is deliberately never read from the projection cache.
        await foreach (var item in canonicalReader.ReadEventsAtAsync(projection, selectedSeconds, cancellationToken))
            yield return item;
    }

    public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) =>
        canonicalReader.ReadReplaySeedAsync(matchId, cancellationToken);

    public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) =>
        canonicalReader.ReadReplayContextAsync(matchId, cancellationToken);

    public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, CancellationToken cancellationToken) =>
        canonicalReader.ReadReplayEventsAtAsync(projection, selectedSeconds, cancellationToken);

    public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, CancellationToken cancellationToken) =>
        canonicalReader.ReadReplayFramesAtAsync(projection, selectedSeconds, cancellationToken);

    public static string BuildKey(MatchId matchId, long canonicalRevision, int selectedSeconds, string algorithmVersion)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (canonicalRevision <= 0) throw new ArgumentOutOfRangeException(nameof(canonicalRevision));
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (string.IsNullOrWhiteSpace(algorithmVersion) || algorithmVersion.Length > 128)
            throw new ArgumentException("Algorithm version must contain 1 to 128 characters.", nameof(algorithmVersion));
        var encodedAlgorithm = Convert.ToBase64String(Encoding.UTF8.GetBytes(algorithmVersion))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"pitchmind:projection:v1:{matchId.Value:N}:{canonicalRevision}:{selectedSeconds}:{encodedAlgorithm}";
    }

    internal static byte[] SerializeCacheValue(SelectedMatchProjection projection) =>
        SerializeCacheValue(projection, projection.AsOfClockSeconds);

    private static byte[] SerializeCacheValue(SelectedMatchProjection projection, int selectedSeconds)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var envelope = new MatchProjectionCacheEnvelope(projection.State.MatchId, projection.CanonicalRevision,
            selectedSeconds, projection.AlgorithmVersion, projection);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    private static SelectedMatchProjection? TryRead(byte[] value, MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<MatchProjectionCacheEnvelope>(value, JsonOptions);
            if (envelope is null || envelope.MatchId != matchId.ToString() || envelope.CanonicalRevision != canonicalRevision ||
                envelope.SelectedTimeSeconds != selectedSeconds || envelope.AlgorithmVersion != algorithmVersion ||
                !Matches(envelope.Projection, matchId, canonicalRevision, selectedSeconds, algorithmVersion)) return null;
            return envelope.Projection;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Matches(SelectedMatchProjection projection, MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion)
    {
        if (projection is null || projection.State is null || string.IsNullOrWhiteSpace(projection.AlgorithmVersion) ||
            string.IsNullOrWhiteSpace(projection.StateSha256) || projection.CanonicalRevision != canonicalRevision ||
            projection.AlgorithmVersion != algorithmVersion ||
            projection.State.MatchId != matchId.ToString() || projection.AsOfClockSeconds > selectedSeconds ||
            projection.AsOfClockSeconds != projection.State.ClockSeconds || projection.AsOfSequence != projection.State.LastSequence ||
            projection.ProjectionRevision <= 0 || projection.StateSha256.Length != 64) return false;
        var stateJson = JsonSerializer.Serialize(projection.State, JsonOptions);
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        return string.Equals(stateHash, projection.StateSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryRemoveAsync(string key, CancellationToken cancellationToken)
    {
        try { await cache.RemoveAsync(key, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* A corrupt entry is ignored even when eviction is temporarily unavailable. */ }
    }

}

internal sealed record MatchProjectionCacheEnvelope(string MatchId, long CanonicalRevision,
    int SelectedTimeSeconds, string AlgorithmVersion, SelectedMatchProjection Projection);
