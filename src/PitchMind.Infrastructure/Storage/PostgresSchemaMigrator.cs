using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Explicit migration operation. Use a migration identity, never the runtime data source.</summary>
public static class PostgresSchemaMigrator
{
    public static async Task ApplyAsync(NpgsqlDataSource migrationDataSource, CancellationToken cancellationToken)
    {
        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(743825900001)", connection, transaction))
            await guard.ExecuteNonQueryAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public const string SchemaSql = """
        CREATE SCHEMA IF NOT EXISTS pitchmind;
        CREATE TABLE IF NOT EXISTS pitchmind.match_events (
            event_id uuid PRIMARY KEY,
            match_id uuid NOT NULL CHECK (match_id <> '00000000-0000-0000-0000-000000000000'),
            sequence bigint NOT NULL CHECK (sequence >= 0),
            event_type integer NOT NULL CHECK (event_type BETWEEN 0 AND 7),
            clock_seconds integer NOT NULL CHECK (clock_seconds >= 0),
            period integer NOT NULL CHECK (period BETWEEN 1 AND 5),
            occurred_at timestamptz NOT NULL,
            payload jsonb NOT NULL,
            received_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (match_id, sequence),
            CHECK (event_id <> '00000000-0000-0000-0000-000000000000')
        );
        CREATE TABLE IF NOT EXISTS pitchmind.schema_versions (
            version integer PRIMARY KEY,
            applied_at timestamptz NOT NULL DEFAULT now()
        );
        INSERT INTO pitchmind.schema_versions(version) VALUES (1) ON CONFLICT DO NOTHING;
        """;
}
