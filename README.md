# PitchMind

Azure-native football intelligence, rebuilt from an evidence-first architecture.

**Status:** Working offline synthetic match slice: deterministic replay, evidence-backed pressing sequence, match APIs and integrated pitch/timeline/analyst UI. Canonical Azure ingestion, Foundry and production certification remain incomplete. See [execution evidence](docs/engineering/EXECUTION-EVIDENCE.md) for verified scope and deployment status.

## Start here
- [Architecture and C4](docs/architecture/README.md)
- [Architecture decision records](docs/adr/README.md)
- [MVP backlog and gates](docs/backlog/MVP-BACKLOG.md)
- [Infrastructure](infra/README.md)
- [Azure Pipelines](pipelines/README.md)
- [Story execution register](docs/backlog/STORY-EXECUTION.md)
- [Football low-level design](docs/architecture/05-football-lld.md)
- [Consolidated API contract](docs/api/API.md)

## Run the simulated match

Requires .NET 10 and Node 22+. No Azure credentials are required for the offline experience.

```bash
dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
# In a second terminal
cd frontend
npm ci
npm run dev
```

Open the Vite URL, play the simulation or choose 55:09, then select **Show me why** and **Explain this moment**. Teams and telemetry are fictional. The deterministic explanation is labeled as a fallback; an active AI model is not implied.

The combined frontend/API image uses the repository root as its build context:

```bash
docker build -f docker/Dockerfile -t pitchmind:dev .
docker run --rm -p 127.0.0.1:5081:8080 pitchmind:dev
python3 scripts/api-smoke.py http://127.0.0.1:5081
```

Open `http://127.0.0.1:5081` for the same-origin application. PostgreSQL integration verification is described in [event ledger](docs/data/CANONICAL-EVENT-LEDGER.md); the offline fixture UI does not read that ledger yet.

## Engineering constraints
- Local: Docker Compose and local emulators/adapters; no required Azure account to run domain tests.
- DEV/STAGE/PROD: Azure services only; no Ollama, local Redis or local PostgreSQL.
- GitHub hosts source; Azure DevOps Pipelines owns CI/CD.
- PostgreSQL is canonical; Redis, AI Search and Data Explorer are projections or ephemeral state.
- AI-generated claims require evidence validation.
- `PitchMind_Old` is read-only reference material; do not bulk-copy code.
