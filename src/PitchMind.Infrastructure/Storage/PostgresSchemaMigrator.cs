using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Explicit migration operation. Use a migration identity, never the runtime data source.</summary>
public static class PostgresSchemaMigrator
{
    public const int CurrentSchemaVersion = 5;

    public static async Task ApplyAsync(NpgsqlDataSource migrationDataSource, CancellationToken cancellationToken)
    {
        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(743825900001)", connection, transaction))
            await guard.ExecuteNonQueryAsync(cancellationToken);

        await using (var bootstrap = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS pitchmind; CREATE TABLE IF NOT EXISTS pitchmind.schema_versions (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());", connection, transaction))
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        var currentVersion = await ReadCurrentVersionAsync(connection, transaction, cancellationToken);
        if (currentVersion > CurrentSchemaVersion)
            throw new InvalidOperationException($"Database schema version {currentVersion} is newer than this application supports ({CurrentSchemaVersion}).");

        if (currentVersion < 1)
        {
            await ExecuteAsync(connection, transaction, SchemaSql, cancellationToken);
            currentVersion = 1;
        }
        if (currentVersion < 2)
            await ExecuteAsync(connection, transaction, Version2Sql, cancellationToken);
        if (currentVersion < 3)
            await ExecuteAsync(connection, transaction, Version3Sql, cancellationToken);
        if (currentVersion < 4)
            await ExecuteAsync(connection, transaction, Version4Sql, cancellationToken);
        if (currentVersion < 5)
            await ExecuteAsync(connection, transaction, Version5Sql, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> ReadCurrentVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(MAX(version), 0) FROM pitchmind.schema_versions", connection, transaction);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    public const string Version2Sql = """
        ALTER TABLE pitchmind.match_events DROP CONSTRAINT IF EXISTS match_events_event_type_check;
        ALTER TABLE pitchmind.match_events ADD CONSTRAINT match_events_event_type_check CHECK (event_type BETWEEN 0 AND 11) NOT VALID;
        ALTER TABLE pitchmind.match_events VALIDATE CONSTRAINT match_events_event_type_check;
        CREATE INDEX IF NOT EXISTS ix_match_events_match_clock_sequence ON pitchmind.match_events(match_id, clock_seconds, sequence);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_match_events_match_event ON pitchmind.match_events(match_id, event_id);

        CREATE TABLE IF NOT EXISTS pitchmind.matches (
            match_id uuid PRIMARY KEY CHECK (match_id <> '00000000-0000-0000-0000-000000000000'),
            competition_id text,
            fixture_version text NOT NULL,
            source_version text NOT NULL,
            source_sha256 char(64) NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
            provenance text NOT NULL,
            pitch_length_metres numeric(7,3) NOT NULL CHECK (pitch_length_metres > 0),
            pitch_width_metres numeric(7,3) NOT NULL CHECK (pitch_width_metres > 0),
            created_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (match_id, source_sha256)
        );

        CREATE TABLE IF NOT EXISTS pitchmind.tracking_frames (
            match_id uuid NOT NULL,
            frame_id text NOT NULL CHECK (length(frame_id) BETWEEN 1 AND 128),
            sequence bigint NOT NULL CHECK (sequence >= 0),
            period integer NOT NULL CHECK (period BETWEEN 1 AND 5),
            clock_seconds integer NOT NULL CHECK (clock_seconds >= 0),
            coordinate_version text NOT NULL,
            quality jsonb NOT NULL,
            source_sha256 char(64) NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
            payload jsonb NOT NULL,
            PRIMARY KEY (match_id, frame_id),
            FOREIGN KEY (match_id, source_sha256) REFERENCES pitchmind.matches(match_id, source_sha256)
        );
        CREATE INDEX IF NOT EXISTS ix_tracking_frames_match_clock ON pitchmind.tracking_frames(match_id, clock_seconds, sequence);

        CREATE TABLE IF NOT EXISTS pitchmind.match_projections (
            match_id uuid NOT NULL,
            revision integer NOT NULL CHECK (revision > 0),
            as_of_sequence bigint NOT NULL CHECK (as_of_sequence >= -1),
            as_of_clock_seconds integer NOT NULL CHECK (as_of_clock_seconds >= 0),
            algorithm_version text NOT NULL,
            source_sha256 char(64) NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
            state_json jsonb NOT NULL,
            state_sha256 char(64) NOT NULL CHECK (state_sha256 ~ '^[0-9a-f]{64}$'),
            created_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (match_id, revision)
        );
        CREATE INDEX IF NOT EXISTS ix_match_projections_match_time ON pitchmind.match_projections(match_id, as_of_clock_seconds, revision);

        CREATE TABLE IF NOT EXISTS pitchmind.insights (
            insight_id text PRIMARY KEY CHECK (length(insight_id) BETWEEN 1 AND 256),
            match_id uuid NOT NULL,
            revision integer NOT NULL CHECK (revision > 0),
            as_of_sequence bigint NOT NULL CHECK (as_of_sequence >= 0),
            detector_version text NOT NULL,
            truth_class text NOT NULL CHECK (truth_class IN ('observed', 'derived', 'evidence-supported-pattern', 'supported-interpretation', 'hypothesis', 'simulated')),
            summary text NOT NULL,
            conditions_json jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (match_id, insight_id),
            UNIQUE (insight_id, match_id, revision),
            FOREIGN KEY (match_id, revision) REFERENCES pitchmind.match_projections(match_id, revision)
        );
        CREATE INDEX IF NOT EXISTS ix_insights_match_revision ON pitchmind.insights(match_id, revision, as_of_sequence);

        CREATE TABLE IF NOT EXISTS pitchmind.insight_evidence (
            insight_id text NOT NULL REFERENCES pitchmind.insights(insight_id),
            evidence_kind text NOT NULL CHECK (evidence_kind IN ('event', 'frame', 'metric', 'condition')),
            evidence_id text NOT NULL CHECK (length(evidence_id) BETWEEN 1 AND 256),
            match_id uuid NOT NULL,
            revision integer NOT NULL CHECK (revision > 0),
            relation text NOT NULL,
            event_id uuid,
            frame_id text,
            PRIMARY KEY (insight_id, evidence_kind, evidence_id),
            CHECK ((evidence_kind = 'event' AND event_id IS NOT NULL AND frame_id IS NULL)
                OR (evidence_kind = 'frame' AND frame_id IS NOT NULL AND event_id IS NULL)
                OR (evidence_kind IN ('metric', 'condition') AND event_id IS NULL AND frame_id IS NULL)),
            CHECK ((event_id IS NULL OR event_id::text = evidence_id)
                AND (frame_id IS NULL OR frame_id = evidence_id)),
            FOREIGN KEY (insight_id, match_id, revision)
                REFERENCES pitchmind.insights(insight_id, match_id, revision),
            FOREIGN KEY (match_id, event_id) REFERENCES pitchmind.match_events(match_id, event_id),
            FOREIGN KEY (match_id, frame_id) REFERENCES pitchmind.tracking_frames(match_id, frame_id)
        );
        CREATE INDEX IF NOT EXISTS ix_insight_evidence_match_revision ON pitchmind.insight_evidence(match_id, revision, insight_id);

        CREATE TABLE IF NOT EXISTS pitchmind.quarantine (
            quarantine_id uuid PRIMARY KEY,
            match_id uuid,
            source_pointer text,
            payload_sha256 char(64) NOT NULL CHECK (payload_sha256 ~ '^[0-9a-f]{64}$'),
            reason_code text NOT NULL CHECK (length(reason_code) BETWEEN 1 AND 128),
            observed_at timestamptz NOT NULL DEFAULT now(),
            retry_status text NOT NULL DEFAULT 'pending' CHECK (retry_status IN ('pending', 'released', 'rejected'))
        );
        CREATE INDEX IF NOT EXISTS ix_quarantine_match_time ON pitchmind.quarantine(match_id, observed_at);

        CREATE TABLE IF NOT EXISTS pitchmind.consumer_checkpoints (
            consumer_id text NOT NULL,
            match_id uuid NOT NULL,
            last_sequence bigint NOT NULL DEFAULT -1 CHECK (last_sequence >= -1),
            updated_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (consumer_id, match_id)
        );

        CREATE TABLE IF NOT EXISTS pitchmind.outbox (
            message_id uuid PRIMARY KEY,
            match_id uuid NOT NULL,
            revision integer NOT NULL CHECK (revision > 0),
            message_kind text NOT NULL,
            payload jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            published_at timestamptz,
            attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            next_attempt_at timestamptz
        );
        CREATE INDEX IF NOT EXISTS ix_outbox_pending ON pitchmind.outbox(next_attempt_at, created_at) WHERE published_at IS NULL;

        INSERT INTO pitchmind.schema_versions(version) VALUES (2) ON CONFLICT DO NOTHING;
        """;

    public const string Version3Sql = """
        ALTER TABLE pitchmind.match_projections ADD CONSTRAINT match_projections_source_fk
            FOREIGN KEY (match_id, source_sha256) REFERENCES pitchmind.matches(match_id, source_sha256);
        ALTER TABLE pitchmind.outbox ADD CONSTRAINT outbox_projection_revision_fk
            FOREIGN KEY (match_id, revision) REFERENCES pitchmind.match_projections(match_id, revision);
        ALTER TABLE pitchmind.outbox ADD COLUMN IF NOT EXISTS claim_token uuid;
        ALTER TABLE pitchmind.outbox ADD COLUMN IF NOT EXISTS lease_expires_at timestamptz;
        ALTER TABLE pitchmind.outbox ADD COLUMN IF NOT EXISTS failed_at timestamptz;
        ALTER TABLE pitchmind.outbox ADD COLUMN IF NOT EXISTS last_error_code text;
        CREATE INDEX IF NOT EXISTS ix_outbox_claimable ON pitchmind.outbox(next_attempt_at, created_at)
            WHERE published_at IS NULL AND failed_at IS NULL;

        CREATE TABLE IF NOT EXISTS pitchmind.consumer_inbox (
            consumer_id text NOT NULL CHECK (length(consumer_id) BETWEEN 1 AND 128),
            message_id uuid NOT NULL,
            processed_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (consumer_id, message_id)
        );

        INSERT INTO pitchmind.schema_versions(version) VALUES (3) ON CONFLICT DO NOTHING;
        """;

    public const string Version4Sql = """
        ALTER TABLE pitchmind.tracking_frames ADD COLUMN canonical_revision integer;
        ALTER TABLE pitchmind.tracking_frames ADD CONSTRAINT tracking_frames_canonical_revision_check
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.tracking_frames ADD CONSTRAINT tracking_frames_projection_revision_fk
            FOREIGN KEY (match_id, canonical_revision) REFERENCES pitchmind.match_projections(match_id, revision);

        -- Existing rows have no recorded admission revision. Leave them unversioned so the
        -- reader fails closed instead of inventing historical visibility during migration.
        CREATE INDEX ix_tracking_frames_match_time_revision_source
            ON pitchmind.tracking_frames(match_id, clock_seconds, canonical_revision, source_sha256, sequence);
        CREATE INDEX ix_tracking_frames_match_revision
            ON pitchmind.tracking_frames(match_id, canonical_revision);

        INSERT INTO pitchmind.schema_versions(version) VALUES (4) ON CONFLICT DO NOTHING;
        """;

    public const string Version5Sql = """
        ALTER TABLE pitchmind.insights ADD COLUMN IF NOT EXISTS limitations_json jsonb;
        ALTER TABLE pitchmind.insights DROP CONSTRAINT IF EXISTS insights_limitations_array_check;
        ALTER TABLE pitchmind.insights ADD CONSTRAINT insights_limitations_array_check
            CHECK (limitations_json IS NULL OR jsonb_typeof(limitations_json) = 'array') NOT VALID;
        ALTER TABLE pitchmind.insights VALIDATE CONSTRAINT insights_limitations_array_check;

        INSERT INTO pitchmind.schema_versions(version) VALUES (5) ON CONFLICT DO NOTHING;
        """;
}
