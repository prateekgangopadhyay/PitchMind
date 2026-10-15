# EP11-S02: Disaster Recovery & Operational Rehearsal Runbook

## Objective
Establish deterministic recovery procedures for canonical match history, projection rebuilding, and zero-downtime service restoration under operational failure scenarios.

## 1. Canonical State Recovery (PostgreSQL)
- **Source of Truth**: Azure Database for PostgreSQL Flexible Server with Automated Backups and Geo-Redundant storage.
- **RPO Target**: <= 5 seconds (transactional WAL replication).
- **RTO Target**: <= 15 minutes.
- **Point-In-Time Restore (PITR) Procedure**:
  1. Identify exact failure timestamp or corrupted revision sequence from `canonical_revisions` audit ledger.
  2. Initiate PITR to a new target server instance using Azure CLI / Portal with specific target timestamp.
  3. Validate ledger head revision and state SHA-256 hash against signed match manifest.
  4. Repoint Container Apps / API connection strings via Key Vault secret update without application recompile.

## 2. Noncanonical Cache Rebuild (Managed Redis)
- **Cache-Aside Architecture**: Redis holds read-accelerated projection snapshots (`pm:proj:{matchId}:{revision}:{clockSeconds}:{algo}`).
- **Failure Resilience**: Complete Redis outage or flush triggers transparent fallback to canonical PostgreSQL read models without impacting match scores or evidence graphs (`CachedMatchProjectionReaderTests.FlushedCacheRebuildsWithoutAlteringCanonicalScoreOrEvidence`).
- **Cold Rebuild Procedure**:
  1. Trigger cache eviction or provision new Redis cluster instance.
  2. Query canonical projections via `CachedMatchProjectionReader`.
  3. Projections are verified via SHA-256 state fingerprint before being repopulated into Redis with default 30-second TTL.

## 3. Asynchronous Dead-Letter Queue (DLQ) Recovery
- **Dead-Letter Sink**: Azure Service Bus dead-letter sub-queues (`match-intelligence-jobs/$DeadLetterQueue`).
- **Poison Handling**: Malformed or unparsable payloads are immediately segregated to prevent queue head-of-line blocking (`BackgroundJobProcessorTests.ProcessAsync_Routes_To_DeadLetterSink_When_Payload_Or_MatchId_Missing`).
- **Replay / Redrive Procedure**:
  1. Investigate DLQ message envelope `DeadLetterReason` and `DeadLetterErrorDescription`.
  2. If root cause was transient dependent outage, trigger Azure Service Bus message redrive to primary queue.
  3. Processor applies idempotent deduplication via `processedJobIds` cache so previously succeeded jobs are not duplicated.

## 4. Promotion & Instant Rollback Procedure
- **Immutable Container Digest**: Releases promote container images strictly by SHA-256 digest (`acr.azurecr.io/pitchmind-api@sha256:...`), never mutable tags (`latest`).
- **Zero-Downtime Rollback**:
  1. Update Azure Container App active revision to prior known-good revision:
     ```bash
     az containerapp revision activate --name pm-api --resource-group pm-prod-rg --revision pm-api--previous-known-good
     ```
  2. Traffic shift: 100% routing to previous revision in < 5 seconds.
  3. No schema rollback needed: database migrations follow expand/contract backward-compatible pattern.
