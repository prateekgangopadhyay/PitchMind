# ADR-003 — Data ownership and projections
Status: Accepted (target), 2026-10-08

**Decision:** PostgreSQL Flexible Server is canonical. Azure Managed Redis is ephemeral/rebuildable. Blob stores raw fixtures and export artifacts. AI Search, Data Explorer and Data Lake are optional projections, not canonical truth.

**Consequences:** Use versioned migrations, separate migration/runtime roles, backups and restoration drills; explicit cache invalidation. Azure Managed Redis resource family is `Microsoft.Cache/redisEnterprise`, not legacy `Microsoft.Cache/redis`.
