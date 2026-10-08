# ADR-007 — Local Docker, Azure-only deployed runtime
Status: Accepted (target), 2026-10-08

**Decision:** Local developers use Docker PostgreSQL/Redis and local adapters. DEV/STAGE/PROD use Azure-managed services, private networking as supported, managed identity/Entra, Key Vault and Azure Monitor. No development-only model or datastore is allowed in deployed environments.

**Consequences:** Config profile tests must reject local service endpoints in Azure environments. Production requires verified RBAC, secretless connections where supported, security scanning and deployment certification.
