namespace PitchMind.Application.Abstractions;

/// <summary>
/// Cache-aside storage interface for immutable match projections (e.g. Azure Managed Redis).
/// A cache failure must never corrupt canonical state or block canonical reads.
/// </summary>
public interface IMatchProjectionCache
{
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
