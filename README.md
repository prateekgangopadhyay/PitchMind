# PitchMind

Azure-native football intelligence, rebuilt from an evidence-first architecture.

**Status:** Architecture baseline and deployable infrastructure skeleton. No claim of a working application, validated Bicep deployment, or Azure release.

## Start here
- [Architecture and C4](docs/architecture/README.md)
- [Architecture decision records](docs/adr/README.md)
- [MVP backlog and gates](docs/backlog/MVP-BACKLOG.md)
- [Infrastructure](infra/README.md)
- [Azure Pipelines](pipelines/README.md)

## Engineering constraints
- Local: Docker Compose and local emulators/adapters; no required Azure account to run domain tests.
- DEV/STAGE/PROD: Azure services only; no Ollama, local Redis or local PostgreSQL.
- GitHub hosts source; Azure DevOps Pipelines owns CI/CD.
- PostgreSQL is canonical; Redis, AI Search and Data Explorer are projections or ephemeral state.
- AI-generated claims require evidence validation.
- `PitchMind_Old` is read-only reference material; do not bulk-copy code.
