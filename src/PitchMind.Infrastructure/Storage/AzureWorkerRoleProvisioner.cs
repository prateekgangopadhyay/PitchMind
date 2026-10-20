using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Maps the EventWorker managed identity to the minimum candidate-ingress database grants.</summary>
public static class AzureWorkerRoleProvisioner
{
    private const string RoleName = "pitchmind_worker";

    public static async Task ProvisionAsync(NpgsqlDataSource migrationDataSource, string principalObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrationDataSource);
        if (!Guid.TryParse(principalObjectId, out var objectId))
            throw new ArgumentException("Worker managed identity object ID must be a GUID.", nameof(principalObjectId));

        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        string? mappedObjectId = null;
        await using (var mapping = new NpgsqlCommand("""
                         SELECT seclabel.label
                         FROM pg_roles r
                         JOIN pg_shseclabel seclabel ON seclabel.classoid = 'pg_authid'::regclass AND seclabel.objoid = r.oid
                         WHERE seclabel.provider = 'pgaadauth' AND r.rolname = @role
                         """, connection))
        {
            mapping.Parameters.AddWithValue("role", RoleName);
            var label = await mapping.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(label))
            {
                foreach (var part in label.Split(','))
                {
                    var trimmed = part.Trim();
                    if (!trimmed.StartsWith("oid=", StringComparison.OrdinalIgnoreCase)) continue;
                    mappedObjectId = trimmed["oid=".Length..].Trim();
                    break;
                }
                mappedObjectId ??= label.Trim();
            }
        }

        if (mappedObjectId is not null &&
            (!Guid.TryParse(mappedObjectId, out var mappedGuid) || mappedGuid != objectId))
            throw new InvalidOperationException("The existing PostgreSQL worker role is mapped to a different Entra identity.");

        if (mappedObjectId is null)
        {
            bool roleExists;
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @role", connection))
            {
                exists.Parameters.AddWithValue("role", RoleName);
                roleExists = await exists.ExecuteScalarAsync(cancellationToken) is not null;
            }

            if (roleExists)
            {
                await MapExistingRoleAsync(connection, objectId, cancellationToken);
            }
            else
            {
                try
                {
                    await using var create = new NpgsqlCommand(
                        "SELECT * FROM pg_catalog.pgaadauth_create_principal_with_oid(@role, @oid, 'service', false, false)", connection);
                    create.Parameters.AddWithValue("role", RoleName);
                    create.Parameters.AddWithValue("oid", objectId.ToString("D"));
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException)
                {
                    await using var createRole = new NpgsqlCommand("CREATE ROLE pitchmind_worker WITH LOGIN", connection);
                    await createRole.ExecuteNonQueryAsync(cancellationToken);
                    await MapExistingRoleAsync(connection, objectId, cancellationToken);
                }
            }
        }

        await using (var harden = new NpgsqlCommand(
                         "ALTER ROLE pitchmind_worker WITH LOGIN PASSWORD NULL NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS",
                         connection))
            await harden.ExecuteNonQueryAsync(cancellationToken);

        await using var grants = new NpgsqlCommand("""
            GRANT CONNECT ON DATABASE pitchmind TO pitchmind_worker;
            GRANT USAGE ON SCHEMA pitchmind TO pitchmind_worker;
            GRANT SELECT, INSERT ON TABLE pitchmind.candidate_feed_outbox TO pitchmind_worker;
            GRANT SELECT, INSERT ON TABLE pitchmind.candidate_feed_poison TO pitchmind_worker;
            GRANT UPDATE (last_observed_at, attempt_count) ON TABLE pitchmind.candidate_feed_poison TO pitchmind_worker;
            """, connection);
        await grants.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MapExistingRoleAsync(NpgsqlConnection connection, Guid objectId,
        CancellationToken cancellationToken)
    {
        await using var map = new NpgsqlCommand(
            "SELECT format('SECURITY LABEL FOR \"pgaadauth\" ON ROLE %I IS %L', @role, @label)", connection);
        map.Parameters.AddWithValue("role", RoleName);
        map.Parameters.AddWithValue("label", $"aadauth,oid={objectId:D},type=service");
        var sql = (string)(await map.ExecuteScalarAsync(cancellationToken)
                           ?? throw new InvalidOperationException("Could not prepare the worker Entra role mapping."));
        await using var apply = new NpgsqlCommand(sql, connection);
        await apply.ExecuteNonQueryAsync(cancellationToken);
    }
}
