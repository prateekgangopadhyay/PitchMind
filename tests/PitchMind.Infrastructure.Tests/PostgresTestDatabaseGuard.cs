using Npgsql;

namespace PitchMind.Infrastructure.Tests;

/// <summary>Allows integration tests to target only a local database with the wrapper's disposable naming convention.</summary>
internal static class PostgresTestDatabaseGuard
{
    private const string DatabasePrefix = "pitchmind_test_";

    public static bool TryValidate(string? connectionString, out string reason)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            reason = "PostgreSQL test connection string is not configured.";
            return false;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            reason = "PostgreSQL test connection string is invalid.";
            return false;
        }

        var host = builder.Host;
        if (host is not ("localhost" or "127.0.0.1" or "::1") || host.Contains(','))
        {
            reason = "PostgreSQL integration tests require a single loopback database host.";
            return false;
        }

        if (builder.TryGetValue("Service", out var service) && service is string { Length: > 0 })
        {
            reason = "PostgreSQL service-file targets are not allowed for integration tests.";
            return false;
        }

        var database = builder.Database ?? string.Empty;
        if (!database.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            database.Length != DatabasePrefix.Length + 32 ||
            !Guid.TryParseExact(database[DatabasePrefix.Length..], "N", out _))
        {
            reason = "PostgreSQL integration tests require a uniquely named pitchmind_test_<32-hex> database.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static string RequireSafeConnectionString(string? connectionString)
    {
        if (TryValidate(connectionString, out var reason))
            return connectionString!;

        throw new InvalidOperationException(reason);
    }
}
