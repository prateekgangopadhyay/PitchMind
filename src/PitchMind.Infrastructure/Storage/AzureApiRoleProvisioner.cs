using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Maps the API managed identity to its restricted Entra-authenticated PostgreSQL role.</summary>
public static class AzureApiRoleProvisioner
{
    private const string RoleName = "pitchmind_api";

    public static async Task ProvisionAsync(NpgsqlDataSource migrationDataSource, string principalObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrationDataSource);
        if (!Guid.TryParse(principalObjectId, out var objectId))
            throw new ArgumentException("API managed identity object ID must be a GUID.", nameof(principalObjectId));

        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        string? mappedObjectId = null;
        await using (var mapping = new NpgsqlCommand(
                         """
                         SELECT seclabel.label
                         FROM pg_roles r
                         JOIN pg_shseclabel seclabel ON seclabel.classoid = 'pg_authid'::regclass AND seclabel.objoid = r.oid
                         WHERE seclabel.provider = 'pgaadauth' AND r.rolname = @role
                         """,
                         connection))
        {
            mapping.Parameters.AddWithValue("role", RoleName);
            var label = await mapping.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(label))
            {
                foreach (var part in label.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("oid=", StringComparison.OrdinalIgnoreCase))
                    {
                        mappedObjectId = trimmed["oid=".Length..].Trim();
                        break;
                    }
                }
                mappedObjectId ??= label.Trim();
            }
        }

        if (mappedObjectId is not null &&
            (!Guid.TryParse(mappedObjectId, out var mappedGuid) || mappedGuid != objectId))
            throw new InvalidOperationException("The existing PostgreSQL API role is mapped to a different Entra identity.");

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
                await using var mapExisting = new NpgsqlCommand(
                    """SELECT format('SECURITY LABEL FOR "pgaadauth" ON ROLE %I IS %L', @role, @label)""", connection);
                mapExisting.Parameters.AddWithValue("role", RoleName);
                mapExisting.Parameters.AddWithValue("label", $"aadauth,oid={objectId:D},type=service");
                var sql = (string)(await mapExisting.ExecuteScalarAsync(cancellationToken)
                                   ?? throw new InvalidOperationException("Could not prepare the API Entra role mapping."));
                await using var applyMapping = new NpgsqlCommand(sql, connection);
                await applyMapping.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                try
                {
                    await using var create = new NpgsqlCommand(
                        "SELECT * FROM pg_catalog.pgaadauth_create_principal_with_oid(@role, @oid, 'service', false, false)",
                        connection);
                    create.Parameters.AddWithValue("role", RoleName);
                    create.Parameters.AddWithValue("oid", objectId.ToString("D"));
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException)
                {
                    await using var createRole = new NpgsqlCommand(
                        """
                        DO $$
                        BEGIN
                            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pitchmind_api') THEN
                                CREATE ROLE pitchmind_api WITH LOGIN;
                            END IF;
                        END $$;
                        """, connection);
                    await createRole.ExecuteNonQueryAsync(cancellationToken);

                    await using var mapExisting = new NpgsqlCommand(
                        """SELECT format('SECURITY LABEL FOR "pgaadauth" ON ROLE %I IS %L', @role, @label)""", connection);
                    mapExisting.Parameters.AddWithValue("role", RoleName);
                    mapExisting.Parameters.AddWithValue("label", $"aadauth,oid={objectId:D},type=service");
                    var sql = (string)(await mapExisting.ExecuteScalarAsync(cancellationToken)
                                       ?? throw new InvalidOperationException("Could not prepare the API Entra role mapping."));
                    await using var applyMapping = new NpgsqlCommand(sql, connection);
                    await applyMapping.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        await using var grants = new NpgsqlCommand("""
            GRANT CONNECT ON DATABASE pitchmind TO pitchmind_api;
            GRANT USAGE ON SCHEMA pitchmind TO pitchmind_api;
            GRANT SELECT ON TABLE pitchmind.schema_versions TO pitchmind_api;
            GRANT SELECT ON TABLE pitchmind.matches, pitchmind.match_events, pitchmind.tracking_frames,
                pitchmind.match_projections, pitchmind.insights, pitchmind.insight_evidence TO pitchmind_api;
            GRANT SELECT, UPDATE (claim_token, lease_expires_at, attempt_count, published_at,
                next_attempt_at, failed_at, last_error_code) ON TABLE pitchmind.outbox TO pitchmind_api;
            GRANT SELECT, INSERT ON TABLE pitchmind.candidate_feed_outbox TO pitchmind_api;
            GRANT UPDATE (claim_token, lease_expires_at, attempt_count, published_at)
                ON TABLE pitchmind.candidate_feed_outbox TO pitchmind_api;
            GRANT SELECT, INSERT ON TABLE pitchmind.candidate_feed_events, pitchmind.candidate_feed_poison TO pitchmind_api;
            GRANT EXECUTE ON FUNCTION pitchmind.clear_candidate_feed() TO pitchmind_api;
            """, connection);
        await grants.ExecuteNonQueryAsync(cancellationToken);

        await using var reassign = new NpgsqlCommand("""
            DO $$
            DECLARE
                reassigned boolean := false;
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'azure_pg_admin') THEN
                    BEGIN
                        EXECUTE 'REASSIGN OWNED BY CURRENT_USER TO azure_pg_admin';
                        reassigned := true;
                    EXCEPTION WHEN OTHERS THEN
                        RAISE WARNING 'Could not reassign ownership to azure_pg_admin: %', SQLERRM;
                    END;
                END IF;

                IF reassigned THEN
                    BEGIN
                        EXECUTE 'DROP OWNED BY CURRENT_USER';
                    EXCEPTION WHEN OTHERS THEN
                        RAISE NOTICE 'Could not drop remaining privileges for CURRENT_USER: %', SQLERRM;
                    END;
                END IF;
            END $$;
            """, connection);
        await reassign.ExecuteNonQueryAsync(cancellationToken);
    }
}

