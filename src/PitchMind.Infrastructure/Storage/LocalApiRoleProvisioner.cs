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
                         "SELECT format('ALTER ROLE pitchmind_api WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB " +
                         "NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', $1)", connection))
        {
            alterRoleSql.Parameters.AddWithValue(password);
            var sql = (string)(await alterRoleSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API role credential update."));
            await using var alterRole = new NpgsqlCommand(sql, connection);
            await alterRole.ExecuteNonQueryAsync(cancellationToken);
        }

        List<string> inheritedRoles = [];
        await using (var membershipQuery = new NpgsqlCommand("""
            SELECT parent.rolname
            FROM pg_auth_members membership
            JOIN pg_roles parent ON parent.oid = membership.roleid
            WHERE membership.member = 'pitchmind_api'::regrole
            ORDER BY parent.rolname
            """, connection))
        await using (var reader = await membershipQuery.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                inheritedRoles.Add(reader.GetString(0));
        }

        foreach (var inheritedRole in inheritedRoles)
        {
            await using var revokeMembershipSql = new NpgsqlCommand(
                "SELECT format('REVOKE %I FROM pitchmind_api', $1)", connection);
            revokeMembershipSql.Parameters.AddWithValue(inheritedRole);
            var sql = (string)(await revokeMembershipSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API role-membership reset."));
            await using var revokeMembership = new NpgsqlCommand(sql, connection);
            await revokeMembership.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var reassignOwnedSql = new NpgsqlCommand(
                         "SELECT format('REASSIGN OWNED BY %I TO %I', 'pitchmind_api', current_user)", connection))
        {
            var sql = (string)(await reassignOwnedSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API ownership reset."));
            await using var reassignOwned = new NpgsqlCommand(sql, connection);
            await reassignOwned.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var revokeDatabasePrivilegesSql = new NpgsqlCommand(
                         "SELECT format('REVOKE ALL PRIVILEGES ON DATABASE %I FROM pitchmind_api', current_database())",
                         connection))
        {
            var sql = (string)(await revokeDatabasePrivilegesSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API database-grant reset."));
            await using var revokeDatabasePrivileges = new NpgsqlCommand(sql, connection);
            await revokeDatabasePrivileges.ExecuteNonQueryAsync(cancellationToken);
        }

        List<string> applicationSchemas = [];
        await using (var schemaQuery = new NpgsqlCommand("""
            SELECT nspname
            FROM pg_namespace
            WHERE nspname <> 'information_schema' AND left(nspname, 3) <> 'pg_'
            ORDER BY nspname
            """, connection))
        await using (var reader = await schemaQuery.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                applicationSchemas.Add(reader.GetString(0));
        }

        foreach (var schemaName in applicationSchemas)
        {
            await using var revokeSchemaPrivilegesSql = new NpgsqlCommand("""
                SELECT format(
                    'REVOKE ALL PRIVILEGES ON SCHEMA %1$I FROM pitchmind_api; ' ||
                    'REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA %1$I FROM pitchmind_api; ' ||
                    'REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA %1$I FROM pitchmind_api; ' ||
                    'REVOKE ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA %1$I FROM pitchmind_api',
                    $1)
                """, connection);
            revokeSchemaPrivilegesSql.Parameters.AddWithValue(schemaName);
            var sql = (string)(await revokeSchemaPrivilegesSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API schema-grant reset."));
            await using var revokeSchemaPrivileges = new NpgsqlCommand(sql, connection);
            await revokeSchemaPrivileges.ExecuteNonQueryAsync(cancellationToken);
        }

        await RevokeLargeObjectPrivilegesAsync(connection, cancellationToken);
        await RevokeDefaultPrivilegesAsync(connection, cancellationToken);

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

    private static async Task RevokeLargeObjectPrivilegesAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        List<uint> largeObjectIds = [];
        await using (var query = new NpgsqlCommand("""
            SELECT DISTINCT metadata.oid
            FROM pg_largeobject_metadata metadata
            CROSS JOIN LATERAL aclexplode(metadata.lomacl) acl
            WHERE acl.grantee = 'pitchmind_api'::regrole
            ORDER BY metadata.oid
            """, connection))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                largeObjectIds.Add(reader.GetFieldValue<uint>(0));
        }

        foreach (var largeObjectId in largeObjectIds)
        {
            await using var revokeSql = new NpgsqlCommand(
                "SELECT format('REVOKE ALL PRIVILEGES ON LARGE OBJECT %s FROM %I', $1::oid, 'pitchmind_api')",
                connection);
            revokeSql.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Oid, largeObjectId);
            var sql = (string)(await revokeSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException(
                                   "Could not prepare LOCAL API large-object grant reset."));
            await using var revoke = new NpgsqlCommand(sql, connection);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task RevokeDefaultPrivilegesAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        List<(string Owner, string? Schema, char ObjectType)> defaults = [];
        await using (var query = new NpgsqlCommand("""
            SELECT owner.rolname, schema.nspname, defaults.defaclobjtype
            FROM pg_default_acl defaults
            JOIN pg_roles owner ON owner.oid = defaults.defaclrole
            LEFT JOIN pg_namespace schema ON schema.oid = defaults.defaclnamespace
            WHERE EXISTS (
                SELECT 1
                FROM aclexplode(defaults.defaclacl) acl
                WHERE acl.grantee = 'pitchmind_api'::regrole
            )
            ORDER BY owner.rolname, schema.nspname NULLS FIRST, defaults.defaclobjtype
            """, connection))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                defaults.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetChar(2)));
        }

        foreach (var (owner, schema, objectType) in defaults)
        {
            var objectClass = objectType switch
            {
                'r' => "TABLES",
                'S' => "SEQUENCES",
                'f' => "FUNCTIONS",
                'T' => "TYPES",
                'n' => "SCHEMAS",
                // PostgreSQL added large-object default privileges in version 17.
                'L' when connection.PostgreSqlVersion.Major >= 17 => "LARGE OBJECTS",
                'L' => throw new InvalidOperationException(
                    "The server reports unsupported large-object default privileges."),
                _ => throw new InvalidOperationException(
                    $"Unsupported PostgreSQL default privilege object type '{objectType}'.")
            };

            var revokeFormat = schema is null
                ? "SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL PRIVILEGES ON " +
                  objectClass + " FROM pitchmind_api', $1)"
                : "SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL PRIVILEGES ON " +
                  objectClass + " FROM pitchmind_api', $1, $2)";
            await using var revokeSql = new NpgsqlCommand(revokeFormat, connection);
            revokeSql.Parameters.AddWithValue(owner);
            if (schema is not null)
                revokeSql.Parameters.AddWithValue(schema);
            var sql = (string)(await revokeSql.ExecuteScalarAsync(cancellationToken)
                               ?? throw new InvalidOperationException("Could not prepare LOCAL API default-ACL reset."));
            await using var revoke = new NpgsqlCommand(sql, connection);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [GeneratedRegex(@"\A[0-9a-f]{48}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ApiPasswordPattern();
}
