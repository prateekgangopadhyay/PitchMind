using Npgsql;

namespace PitchMind.Api;

/// <summary>Allows local outbox mutation only against the host-loopback endpoints used by Docker Compose.</summary>
public static class LocalPostgresConnectionPolicy
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "localhost",
        "::1"
    };

    public static bool IsLoopbackConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            var host = new NpgsqlConnectionStringBuilder(connectionString).Host?.Trim();
            // Npgsql supports comma-separated failover hosts; reject the entire string unless there is one host.
            return host is not null && !host.Contains(',', StringComparison.Ordinal) && AllowedHosts.Contains(host);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
