using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

public static class DataClearingService
{
    public static async Task<int> ClearAllAsync(
        NpgsqlDataSource? dataSource,
        IMatchProjectionCache? cache,
        CancellationToken cancellationToken = default)
    {
        var cleared = 0;
        if (dataSource is not null)
        {
            cleared = await ClearDatabaseAsync(dataSource, cancellationToken).ConfigureAwait(false);
        }

        if (cache is not null)
        {
            await cache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        return cleared;
    }

    public static async Task<int> ClearDatabaseAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var tables = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'pitchmind' AND tablename <> 'schema_versions';", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0) return 0;

        var quoted = string.Join(", ", tables.Select(t => $"pitchmind.\"{t.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
        await using var truncateCmd = new NpgsqlCommand($"TRUNCATE TABLE {quoted} CASCADE;", connection);
        await truncateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return tables.Count;
    }
}
