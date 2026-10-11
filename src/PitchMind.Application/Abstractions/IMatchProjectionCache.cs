namespace PitchMind.Application.Abstractions;

/// <summary>A disposable byte cache for immutable projections; PostgreSQL remains the source of truth.</summary>
public interface IMatchProjectionCache
{
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
