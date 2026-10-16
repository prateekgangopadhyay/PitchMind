using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Explicit migration operation. Use a migration identity, never the runtime data source.</summary>
public static class PostgresSchemaMigrator
{
    public const int CurrentSchemaVersion = 11;

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
        if (currentVersion < 6)
            await ExecuteAsync(connection, transaction, Version6Sql, cancellationToken);
        if (currentVersion < 7)
            await ExecuteAsync(connection, transaction, Version7Sql, cancellationToken);
        if (currentVersion < 8)
            await ExecuteAsync(connection, transaction, Version8Sql, cancellationToken);
        if (currentVersion < 9)
            await ExecuteAsync(connection, transaction, Version9Sql, cancellationToken);
        if (currentVersion < 10)
            await ExecuteAsync(connection, transaction, QuarantineV10Sql, cancellationToken);
        if (currentVersion < 11)
            await ExecuteAsync(connection, transaction, ConsumerCheckpointsV11Sql, cancellationToken);
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

    /// <summary>
    /// Separates PostgreSQL admission identity from projection algorithm revision. Legacy rows
    /// remain NULL because assigning them an admission revision during migration would invent provenance.
    /// New canonical writers must assign a positive revision to every event/frame/projection/insight/outbox row.
    /// </summary>
    public const string Version3Sql = """
        ALTER TABLE pitchmind.match_events ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.match_events ADD CONSTRAINT match_events_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.match_events ADD CONSTRAINT match_events_match_event_revision_unique
            UNIQUE (match_id, event_id, canonical_revision);

        ALTER TABLE pitchmind.tracking_frames ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.tracking_frames ADD CONSTRAINT tracking_frames_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.tracking_frames ADD CONSTRAINT tracking_frames_match_frame_revision_unique
            UNIQUE (match_id, frame_id, canonical_revision);

        ALTER TABLE pitchmind.match_projections ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.match_projections ADD CONSTRAINT match_projections_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.match_projections ADD CONSTRAINT match_projections_match_projection_revision_unique
            UNIQUE (match_id, revision, canonical_revision);

        ALTER TABLE pitchmind.insights ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.insights ADD CONSTRAINT insights_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.insights ADD CONSTRAINT insights_match_canonical_revision_unique
            UNIQUE (match_id, insight_id, canonical_revision);
        ALTER TABLE pitchmind.insights ADD CONSTRAINT insights_projection_admission_revision_fk
            FOREIGN KEY (match_id, revision, canonical_revision)
            REFERENCES pitchmind.match_projections(match_id, revision, canonical_revision);

        ALTER TABLE pitchmind.insight_evidence ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.insight_evidence ADD CONSTRAINT insight_evidence_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.insight_evidence ADD CONSTRAINT insight_evidence_insight_admission_revision_fk
            FOREIGN KEY (match_id, insight_id, canonical_revision)
            REFERENCES pitchmind.insights(match_id, insight_id, canonical_revision);
        ALTER TABLE pitchmind.insight_evidence ADD CONSTRAINT insight_evidence_event_admission_revision_fk
            FOREIGN KEY (match_id, event_id, canonical_revision)
            REFERENCES pitchmind.match_events(match_id, event_id, canonical_revision);
        ALTER TABLE pitchmind.insight_evidence ADD CONSTRAINT insight_evidence_frame_admission_revision_fk
            FOREIGN KEY (match_id, frame_id, canonical_revision)
            REFERENCES pitchmind.tracking_frames(match_id, frame_id, canonical_revision);

        ALTER TABLE pitchmind.outbox ADD COLUMN IF NOT EXISTS canonical_revision bigint;
        ALTER TABLE pitchmind.outbox ADD CONSTRAINT outbox_canonical_revision_positive
            CHECK (canonical_revision IS NULL OR canonical_revision > 0);
        ALTER TABLE pitchmind.outbox ADD CONSTRAINT outbox_match_canonical_revision_fk
            FOREIGN KEY (match_id, revision, canonical_revision)
            REFERENCES pitchmind.match_projections(match_id, revision, canonical_revision);

        CREATE INDEX IF NOT EXISTS ix_match_events_match_canonical_revision
            ON pitchmind.match_events(match_id, canonical_revision, sequence) WHERE canonical_revision IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_tracking_frames_match_canonical_revision
            ON pitchmind.tracking_frames(match_id, canonical_revision, sequence) WHERE canonical_revision IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_match_projections_match_canonical_revision
            ON pitchmind.match_projections(match_id, canonical_revision, revision) WHERE canonical_revision IS NOT NULL;
        INSERT INTO pitchmind.schema_versions(version) VALUES (3) ON CONFLICT DO NOTHING;
        """;

    /// <summary>Ensures projection provenance always refers to the registered immutable source identity.</summary>
    public const string Version4Sql = """
        ALTER TABLE pitchmind.match_projections
            ADD CONSTRAINT match_projections_match_source_fk
            FOREIGN KEY (match_id, source_sha256)
            REFERENCES pitchmind.matches(match_id, source_sha256) NOT VALID;
        INSERT INTO pitchmind.schema_versions(version) VALUES (4) ON CONFLICT DO NOTHING;
        """;

    public const string Version5Sql = """
        CREATE UNIQUE INDEX IF NOT EXISTS ux_match_events_match_canonical_revision
            ON pitchmind.match_events(match_id, canonical_revision) WHERE canonical_revision IS NOT NULL;
        ALTER TABLE pitchmind.outbox ADD CONSTRAINT outbox_event_admission_revision_fk
            FOREIGN KEY (match_id, message_id, canonical_revision)
            REFERENCES pitchmind.match_events(match_id, event_id, canonical_revision);
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
        INSERT INTO pitchmind.schema_versions(version) VALUES (5) ON CONFLICT DO NOTHING;
        """;

    /// <summary>Persists explicit competition and re-entry rule context without guessing legacy values.</summary>
    public const string Version6Sql = """
        ALTER TABLE pitchmind.matches ADD COLUMN IF NOT EXISTS competition_rules_version text;
        ALTER TABLE pitchmind.matches ADD COLUMN IF NOT EXISTS max_substitute_reentries_per_player integer;
        ALTER TABLE pitchmind.matches DROP CONSTRAINT IF EXISTS matches_competition_rules_version_nonempty;
        ALTER TABLE pitchmind.matches ADD CONSTRAINT matches_competition_rules_version_nonempty
            CHECK (competition_rules_version IS NULL OR length(competition_rules_version) BETWEEN 1 AND 128) NOT VALID;
        ALTER TABLE pitchmind.matches VALIDATE CONSTRAINT matches_competition_rules_version_nonempty;
        ALTER TABLE pitchmind.matches DROP CONSTRAINT IF EXISTS matches_substitute_reentry_limit_nonnegative;
        ALTER TABLE pitchmind.matches ADD CONSTRAINT matches_substitute_reentry_limit_nonnegative
            CHECK (max_substitute_reentries_per_player IS NULL OR max_substitute_reentries_per_player >= 0) NOT VALID;
        ALTER TABLE pitchmind.matches VALIDATE CONSTRAINT matches_substitute_reentry_limit_nonnegative;
        ALTER TABLE pitchmind.matches DROP CONSTRAINT IF EXISTS matches_reentry_limit_requires_version;
        ALTER TABLE pitchmind.matches ADD CONSTRAINT matches_reentry_limit_requires_version
            CHECK (max_substitute_reentries_per_player IS NULL OR competition_rules_version IS NOT NULL) NOT VALID;
        ALTER TABLE pitchmind.matches VALIDATE CONSTRAINT matches_reentry_limit_requires_version;
        INSERT INTO pitchmind.schema_versions(version) VALUES (6) ON CONFLICT DO NOTHING;
        """;
    /// <summary>Stores replay inputs without backfilling legacy rows with invented source context.</summary>
    public const string Version7Sql = """
        ALTER TABLE pitchmind.matches ADD COLUMN IF NOT EXISTS replay_seed_json jsonb;
        ALTER TABLE pitchmind.match_events ADD COLUMN IF NOT EXISTS source_event_json jsonb;
        INSERT INTO pitchmind.schema_versions(version) VALUES (7) ON CONFLICT DO NOTHING;
        """;

    /// <summary>Stores the immutable, versioned descriptor required to reconstruct detector inputs.</summary>
    public const string Version8Sql = """
        ALTER TABLE pitchmind.matches ADD COLUMN IF NOT EXISTS replay_context_json jsonb;
        INSERT INTO pitchmind.schema_versions(version) VALUES (8) ON CONFLICT DO NOTHING;
        """;

    /// <summary>Separates an insight's admission revision from the revisions of its evidence sources.</summary>
    public const string Version9Sql = """
        ALTER TABLE pitchmind.insight_evidence ADD COLUMN IF NOT EXISTS evidence_canonical_revision bigint;
        UPDATE pitchmind.insight_evidence e SET evidence_canonical_revision = me.canonical_revision
        FROM pitchmind.match_events me
        WHERE e.evidence_kind = 'event' AND e.match_id = me.match_id AND e.event_id = me.event_id
          AND e.canonical_revision = me.canonical_revision
          AND e.evidence_canonical_revision IS NULL;
        UPDATE pitchmind.insight_evidence e SET evidence_canonical_revision = tf.canonical_revision
        FROM pitchmind.tracking_frames tf
        WHERE e.evidence_kind = 'frame' AND e.match_id = tf.match_id AND e.frame_id = tf.frame_id
          AND e.canonical_revision = tf.canonical_revision
          AND e.evidence_canonical_revision IS NULL;

        ALTER TABLE pitchmind.insight_evidence
            DROP CONSTRAINT IF EXISTS insight_evidence_event_admission_revision_fk;
        ALTER TABLE pitchmind.insight_evidence
            DROP CONSTRAINT IF EXISTS insight_evidence_frame_admission_revision_fk;
        ALTER TABLE pitchmind.insight_evidence
            ADD CONSTRAINT insight_evidence_source_revision_check CHECK (
                (evidence_kind IN ('event', 'frame') AND evidence_canonical_revision IS NOT NULL AND evidence_canonical_revision > 0)
                OR (evidence_kind NOT IN ('event', 'frame') AND evidence_canonical_revision IS NULL));
        ALTER TABLE pitchmind.insight_evidence
            ADD CONSTRAINT insight_evidence_event_source_revision_fk
            FOREIGN KEY (match_id, event_id, evidence_canonical_revision)
            REFERENCES pitchmind.match_events(match_id, event_id, canonical_revision);
        ALTER TABLE pitchmind.insight_evidence
            ADD CONSTRAINT insight_evidence_frame_source_revision_fk
            FOREIGN KEY (match_id, frame_id, evidence_canonical_revision)
            REFERENCES pitchmind.tracking_frames(match_id, frame_id, canonical_revision);
        INSERT INTO pitchmind.schema_versions(version) VALUES (9) ON CONFLICT DO NOTHING;
        """;

    /// <summary>Upgrades the legacy quarantine table to immutable source-rejection evidence.</summary>
    public const string QuarantineV10Sql = """
        DO $migration$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pitchmind.schema_versions WHERE version = 10) THEN
                IF to_regclass('pitchmind.quarantine') IS NULL THEN
                    RAISE EXCEPTION 'schema v10 requires pitchmind.quarantine from schema v2';
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'quarantine_id')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'record_id') THEN
                    ALTER TABLE pitchmind.quarantine RENAME COLUMN quarantine_id TO record_id;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'source_pointer')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'source_ptr') THEN
                    ALTER TABLE pitchmind.quarantine RENAME COLUMN source_pointer TO source_ptr;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'payload_sha256')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'payload_hash') THEN
                    ALTER TABLE pitchmind.quarantine RENAME COLUMN payload_sha256 TO payload_hash;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'reason_code')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = 'quarantine' AND column_name = 'reason') THEN
                    ALTER TABLE pitchmind.quarantine RENAME COLUMN reason_code TO reason;
                END IF;

                -- The previous schema required hashes and assigned retry state before source
                -- bytes or a retry workflow necessarily existed. Preserve legacy values while
                -- allowing new records to represent unavailable data honestly.
                ALTER TABLE pitchmind.quarantine ALTER COLUMN payload_hash DROP NOT NULL;
                ALTER TABLE pitchmind.quarantine ALTER COLUMN retry_status DROP NOT NULL;
                ALTER TABLE pitchmind.quarantine ALTER COLUMN retry_status DROP DEFAULT;
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_payload_sha256_check;
                -- Test/repair tooling may rewind only the version marker. Rebuilding our named
                -- constraints keeps the migration safe to replay against the already-upgraded table.
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_payload_hash_format;
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_record_id_nonzero;
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_match_id_nonzero;
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_source_ptr_length;
                ALTER TABLE pitchmind.quarantine DROP CONSTRAINT IF EXISTS quarantine_reason_code_format;
                ALTER TABLE pitchmind.quarantine ADD CONSTRAINT quarantine_payload_hash_format
                    CHECK (payload_hash IS NULL OR payload_hash ~ '^[0-9A-Fa-f]{64}$') NOT VALID;
                ALTER TABLE pitchmind.quarantine ADD CONSTRAINT quarantine_record_id_nonzero
                    CHECK (record_id <> '00000000-0000-0000-0000-000000000000');
                ALTER TABLE pitchmind.quarantine ADD CONSTRAINT quarantine_match_id_nonzero
                    CHECK (match_id IS NULL OR match_id <> '00000000-0000-0000-0000-000000000000') NOT VALID;
                ALTER TABLE pitchmind.quarantine ADD CONSTRAINT quarantine_source_ptr_length
                    CHECK (source_ptr IS NOT NULL AND length(source_ptr) BETWEEN 1 AND 2048) NOT VALID;
                ALTER TABLE pitchmind.quarantine ADD CONSTRAINT quarantine_reason_code_format
                    CHECK (reason ~ '^[A-Z][A-Z0-9_]{0,99}$') NOT VALID;

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
                DROP TRIGGER IF EXISTS quarantine_no_truncate ON pitchmind.quarantine;
                CREATE TRIGGER quarantine_no_truncate
                    BEFORE TRUNCATE ON pitchmind.quarantine
                    FOR EACH STATEMENT EXECUTE FUNCTION pitchmind.reject_quarantine_mutation();
                INSERT INTO pitchmind.schema_versions(version) VALUES (10);
            END IF;
        END
        $migration$;
        """;

    /// <summary>Replaces the unused per-match placeholder with a durable partition cursor.</summary>
    public const string ConsumerCheckpointsV11Sql = """
        DO $$
        BEGIN
            IF to_regclass('pitchmind.consumer_checkpoints') IS NOT NULL
               AND to_regclass('pitchmind.consumer_checkpoints_legacy_v10') IS NULL THEN
                ALTER TABLE pitchmind.consumer_checkpoints RENAME TO consumer_checkpoints_legacy_v10;
            END IF;
        END
        $$;

        CREATE TABLE IF NOT EXISTS pitchmind.consumer_checkpoints (
            consumer_id text NOT NULL CHECK (length(consumer_id) BETWEEN 1 AND 128),
            partition_id text NOT NULL CHECK (length(partition_id) BETWEEN 1 AND 128),
            last_offset text,
            last_broker_sequence_number bigint NOT NULL DEFAULT -1 CHECK (last_broker_sequence_number >= -1),
            last_match_id uuid,
            last_message_id uuid,
            last_canonical_sequence bigint CHECK (last_canonical_sequence IS NULL OR last_canonical_sequence >= 0),
            updated_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (consumer_id, partition_id),
            CHECK ((last_broker_sequence_number = -1 AND last_offset IS NULL AND last_match_id IS NULL
                    AND last_message_id IS NULL AND last_canonical_sequence IS NULL)
                OR (last_broker_sequence_number >= 0 AND length(last_offset) BETWEEN 1 AND 128
                    AND last_match_id IS NOT NULL AND last_match_id <> '00000000-0000-0000-0000-000000000000'
                    AND last_message_id IS NOT NULL AND last_message_id <> '00000000-0000-0000-0000-000000000000'
                    AND last_canonical_sequence IS NOT NULL))
        );
        INSERT INTO pitchmind.schema_versions(version) VALUES (11) ON CONFLICT DO NOTHING;
        """;
}
