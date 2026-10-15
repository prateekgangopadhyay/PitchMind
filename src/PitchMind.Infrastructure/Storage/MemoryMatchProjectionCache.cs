using System.Collections.Concurrent;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Thread-safe in-memory cache implementation of IMatchProjectionCache for testing and offline local execution.
/// </summary>
public sealed class MemoryMatchProjectionCache : IMatchProjectionCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);

    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return Task.FromResult<byte[]?>(entry.Data);
            entries.TryRemove(key, out _);
        }
        return Task.FromResult<byte[]?>(null);
    }

    public Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        entries[key] = new CacheEntry(value, DateTimeOffset.UtcNow.Add(timeToLive));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public void Clear() => entries.Clear();

    private sealed record CacheEntry(byte[] Data, DateTimeOffset ExpiresAtUtc);
}
