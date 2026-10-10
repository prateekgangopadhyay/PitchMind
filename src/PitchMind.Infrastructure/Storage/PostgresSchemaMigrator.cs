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
        await using var migration = new NpgsqlCommand(EventTypeRangeV2Sql, connection, transaction);
        await migration.ExecuteNonQueryAsync(cancellationToken);
        await using var quarantineMigration = new NpgsqlCommand(QuarantineV3Sql, connection, transaction);
        await quarantineMigration.ExecuteNonQueryAsync(cancellationToken);
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

    /// <summary>Expands the v1 event type check to cover every currently defined MatchEventType value.</summary>
    public const string EventTypeRangeV2Sql = """
        DO $migration$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pitchmind.schema_versions WHERE version = 2) THEN
                ALTER TABLE pitchmind.match_events
                    DROP CONSTRAINT IF EXISTS match_events_event_type_check;
                ALTER TABLE pitchmind.match_events
                    ADD CONSTRAINT match_events_event_type_check CHECK (event_type BETWEEN 0 AND 10);
                INSERT INTO pitchmind.schema_versions(version) VALUES (2);
            END IF;
        END
        $migration$;
        """;

    /// <summary>Creates append-only quarantine evidence without defining retry or reconciliation behavior.</summary>
    public const string QuarantineV3Sql = """
        DO $migration$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pitchmind.schema_versions WHERE version = 3) THEN
                CREATE TABLE IF NOT EXISTS pitchmind.quarantine (
                    record_id uuid PRIMARY KEY CHECK (record_id <> '00000000-0000-0000-0000-000000000000'),
                    match_id uuid NULL CHECK (match_id IS NULL OR match_id <> '00000000-0000-0000-0000-000000000000'),
                    source_ptr text NOT NULL CHECK (length(source_ptr) BETWEEN 1 AND 2048),
                    payload_hash char(64) NULL CHECK (payload_hash IS NULL OR payload_hash ~ '^[0-9A-Fa-f]{64}$'),
                    reason text NOT NULL CHECK (reason ~ '^[A-Z][A-Z0-9_]{0,99}$'),
                    observed_at timestamptz NOT NULL,
                    retry_status text NULL
                );
                CREATE INDEX IF NOT EXISTS quarantine_match_observed_idx
                    ON pitchmind.quarantine(match_id, observed_at)
                    WHERE match_id IS NOT NULL;
                CREATE OR REPLACE FUNCTION pitchmind.reject_quarantine_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                    RAISE EXCEPTION 'quarantine evidence records are immutable'
                        USING ERRCODE = '55000';
                END;
                $function$;
                DROP TRIGGER IF EXISTS quarantine_immutable ON pitchmind.quarantine;
                CREATE TRIGGER quarantine_immutable
                    BEFORE UPDATE OR DELETE ON pitchmind.quarantine
                    FOR EACH ROW EXECUTE FUNCTION pitchmind.reject_quarantine_mutation();
                INSERT INTO pitchmind.schema_versions(version) VALUES (3);
            END IF;
        END
        $migration$;
        """;
}
