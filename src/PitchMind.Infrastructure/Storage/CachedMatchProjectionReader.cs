using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Cache-aside wrapper for exact-revision snapshots. A Redis adapter can implement IMatchProjectionCache;
/// errors and invalid payloads always fall through to the canonical PostgreSQL reader.
/// </summary>
public sealed class CachedMatchProjectionReader(
    IMatchProjectionReader canonicalReader,
    IMatchProjectionCache cache,
    TimeSpan? timeToLive = null) : IMatchProjectionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);
    private const int MaximumCacheEntryBytes = 1_048_576;

    public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
        CancellationToken cancellationToken) => canonicalReader.GetAtAsync(matchId, selectedSeconds, maximumRevision, cancellationToken);

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
            // Cache transport and data errors degrade to the canonical store.
        }

        var projection = await canonicalReader.GetAtCanonicalRevisionAsync(
            matchId, canonicalRevision, selectedSeconds, algorithmVersion, cancellationToken);
        if (projection is null) return null;
        if (!Matches(projection, matchId, canonicalRevision, selectedSeconds, algorithmVersion))
            throw new InvalidDataException("Canonical reader returned a projection outside the requested cache identity.");

        var envelope = new CacheEnvelope(matchId.ToString(), canonicalRevision, selectedSeconds, algorithmVersion, projection);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
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
                // A failed cache write must not fail a successful canonical read.
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

    private static SelectedMatchProjection? TryRead(byte[] value, MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(value, JsonOptions);
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
            projection.Revision <= 0 || projection.StateSha256.Length != 64) return false;
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

    private sealed record CacheEnvelope(string MatchId, long CanonicalRevision, int SelectedTimeSeconds,
        string AlgorithmVersion, SelectedMatchProjection Projection);
}
