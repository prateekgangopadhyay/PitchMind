-- Local-only runtime identity. Applied by the PostgreSQL owner during dev-up and after migrations.
-- This script is intentionally separate from SQL consumed by Npgsql integration tests.
\set ON_ERROR_STOP on
\getenv worker_password PITCHMIND_WORKER_PASSWORD

SELECT format('CREATE ROLE pitchmind_worker LOGIN PASSWORD %L', :'worker_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pitchmind_worker')
\gexec
ALTER ROLE pitchmind_worker WITH LOGIN PASSWORD :'worker_password';

GRANT USAGE ON SCHEMA pitchmind_local TO pitchmind_worker;
GRANT SELECT, INSERT ON pitchmind_local.outbox_deliveries TO pitchmind_worker;

-- A fresh database has no canonical schema until dev-migrate.py is explicitly run.
DO $$
BEGIN
    IF to_regclass('pitchmind.outbox') IS NOT NULL THEN
        EXECUTE 'GRANT USAGE ON SCHEMA pitchmind TO pitchmind_worker';
        EXECUTE 'GRANT SELECT ON pitchmind.outbox TO pitchmind_worker';
        EXECUTE 'GRANT UPDATE (claim_token, lease_expires_at, attempt_count, published_at, next_attempt_at, failed_at, last_error_code) ON pitchmind.outbox TO pitchmind_worker';
    END IF;
END $$;
