# M02 — Local Docker infrastructure and Azure DevOps validation

**Status:** Implementation proposed in feature PR; executable CI and ARM64 smoke evidence pending.

## Boundaries
Local Docker runs PostgreSQL 17 and Redis 7.4. Neither local component is used by deployed Azure environments. No Azure SDK credential, managed service emulator, or cloud deployment is required to run M01 API locally. M03 implements Azure-managed infrastructure; M04/M05 wire real data adapters.

## macOS ARM64 onboarding
Prerequisites: Docker Desktop running, .NET 10 SDK, Node 22+, Git, OpenSSL.

```bash
git switch feat/m02-local-docker-azure-pipelines
chmod +x scripts/*.sh
./scripts/dev-up.sh
./scripts/dev-health.sh
./scripts/validate-all.sh
dotnet run --project src/PitchMind.Api
# In a second terminal:
cd frontend && npm run dev
# When finished:
./scripts/dev-down.sh
```

## Security and operations
- Passwords are generated locally using OpenSSL; `docker/.env` is gitignored and not committed.
- PostgreSQL/Redis host ports bind to loopback only; containers use persistent named volumes.
- Container images use architecture-compatible multi-arch official images; CI smoke tests run on Ubuntu hosted agent.
- `dev-down.sh` preserves data; `docker compose down -v` destroys data.
- Local development passwords must not be reused in Azure.
- Azure DevOps pipelines are validation-only; production approvals and cloud service connections are M03/M13 work.

## CI quality gates
Azure DevOps validates .NET restore/build/tests, React install/build/test/lint, Docker Compose health, repository baseline, and Bicep compile. No GitHub Actions. Configure GitHub branch protection with Azure Pipelines as a required check. GitHub-hosted PR validation may require explicit Azure DevOps GitHub integration configuration.

## Known limitations
- M01 API and workers do not yet consume PostgreSQL or Redis. Containers are verified infrastructure only.
- Frontend package-lock.json is not yet committed; CI uses npm install until a reviewed lockfile is generated and committed. Deterministic npm ci is the follow-up gate.
- Existing Bicep baseline was not validated during M01; compilation may reveal errors to resolve before M02 acceptance.
- No performance, end-to-end Azure, or production deployment evidence exists.

## Acceptance
- [x] Docker Compose definitions, health checks and local scripts authored.
- [x] Azure DevOps validation jobs authored.
- [ ] ARM64 Docker smoke executed.
- [ ] Backend/frontend clean runner validation passed.
- [ ] Bicep compiled without errors.
- [ ] Azure DevOps PR check observed green.
- [ ] Review and merge.
