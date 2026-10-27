using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

public static class DataClearingService
{
    /// <summary>
    /// Clears disposable candidate-feed records through the migration-owned, narrowly scoped
    /// function. Canonical match history and schema metadata remain append-only/preserved.
    /// </summary>
    public static async Task<long> ClearCandidateDataAsync(
        NpgsqlDataSource dataSource,
        IMatchProjectionCache cache,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(cache);

        try
        {
            await cache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ProjectionCacheUnavailableException)
        {
            // Cache is degraded or temporarily unavailable; proceed with canonical PostgreSQL clear.
        }
        catch (Exception)
        {
            // Transient cache failure must not prevent canonical data clearing.
        }

        await using var command = dataSource.CreateCommand("SELECT pitchmind.clear_candidate_feed();");
        var clearedRecords = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The candidate-feed clear operation returned no result."));
        return clearedRecords;
    }
}
