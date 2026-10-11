-- Development-only durable handoff target for the local outbox publisher.
-- Azure DEV/STAGE/PRODUCTION use managed transports and must not register this adapter.
CREATE SCHEMA IF NOT EXISTS pitchmind_local;

CREATE TABLE IF NOT EXISTS pitchmind_local.outbox_deliveries (
    message_id uuid PRIMARY KEY,
    payload jsonb NOT NULL,
    delivered_at timestamptz NOT NULL DEFAULT now()
);
