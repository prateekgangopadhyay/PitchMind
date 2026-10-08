# Security baseline

- Use Entra workload identity federation for Azure Pipelines; no long-lived service principal secrets.
- Runtime managed identity + least-privilege RBAC; Key Vault for unavoidable secrets.
- PostgreSQL and Redis private networking and authentication before production.
- Deny unauthenticated export/replay administration.
- Model prompts and tool outputs are untrusted; evidence gate before publishing.
- SBOM, dependency, secret and IaC scanning in PR and release gates.
- Logs redact credentials and personal data; propagate matchId, eventId and traceId.
- Explicit backup/restore drills, budget alerts and incident response playbooks.
