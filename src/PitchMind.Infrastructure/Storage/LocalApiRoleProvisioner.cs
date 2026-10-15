using System.Text.RegularExpressions;
using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Creates or refreshes the least-privilege LOCAL API role after explicit schema migration.</summary>
public static partial class LocalApiRoleProvisioner
{
    private const string RoleName = "pitchmind_api";

    public static async Task ProvisionAsync(NpgsqlDataSource migrationDataSource, string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrationDataSource);
        if (string.IsNullOrEmpty(password) || !ApiPasswordPattern().IsMatch(password) || password.Distinct().Count() < 12)
            throw new ArgumentException(
                "LOCAL API password must be 48 lowercase hexadecimal characters with at least 12 unique hex digits.",
                nameof(password));

        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        await using (var createRoleSql = new NpgsqlCommand(
                         "SELECT format('CREATE ROLE pitchmind_api LOGIN PASSWORD %L', $1) " +
                         "WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pitchmind_api')",
                         connection))
        {
            createRoleSql.Parameters.AddWithValue(password);
            if (await createRoleSql.ExecuteScalarAsync(cancellationToken) is string createSql)
            {
                await using var createRole = new NpgsqlCommand(createSql, connection);
                await createRole.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var alterRoleSql = new NpgsqlCommand(
                         "SELECT format('ALTER ROLE pitchmind_api WITH LOGIN PASSWORD %L', $1)", connection))
        {
            alterRoleSql.Parameters.AddWithValue(password);
            var sql = (string)(await alterRoleSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API role credential update."));
            await using var alterRole = new NpgsqlCommand(sql, connection);
            await alterRole.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var grantConnectSql = new NpgsqlCommand(
                         "SELECT format('GRANT CONNECT ON DATABASE %I TO pitchmind_api', current_database())",
                         connection))
        {
            var sql = (string)(await grantConnectSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API database grant."));
            await using var grantConnect = new NpgsqlCommand(sql, connection);
            await grantConnect.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var grants = new NpgsqlCommand("""
            GRANT USAGE ON SCHEMA pitchmind TO pitchmind_api;
            GRANT SELECT ON TABLE pitchmind.schema_versions TO pitchmind_api;
            GRANT SELECT ON TABLE pitchmind.matches, pitchmind.match_events, pitchmind.tracking_frames,
                pitchmind.match_projections, pitchmind.insights, pitchmind.insight_evidence TO pitchmind_api;
            GRANT SELECT ON TABLE pitchmind.outbox TO pitchmind_api;
            GRANT UPDATE (claim_token, lease_expires_at, attempt_count, published_at, next_attempt_at,
                failed_at, last_error_code) ON TABLE pitchmind.outbox TO pitchmind_api;
            """, connection);
        await grants.ExecuteNonQueryAsync(cancellationToken);
    }

    [GeneratedRegex("^[0-9a-f]{48}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiPasswordPattern();
}
