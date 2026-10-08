# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17 and Redis 7.4 run in Docker Desktop. The ports bind only to 127.0.0.1. Named volumes persist data. **These are development-only containers**; Azure environments use managed Azure services.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
./scripts/dev-down.sh
```

The first run creates docker/.env with random passwords. It is gitignored. Never share its contents. To reset data deliberately: `docker compose --env-file docker/.env -f docker/compose.yml down -v` (destructive).

The M01 API and workers still use scaffold adapters; these database/cache containers are not yet wired into canonical persistence or caching. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
