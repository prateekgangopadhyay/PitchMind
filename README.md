# PitchMind

Azure-native football intelligence, rebuilt from an evidence-first architecture.

**Status:** Working offline synthetic match slice: deterministic replay, evidence-backed pressing sequence, match APIs and integrated pitch/timeline/analyst UI. Canonical Azure ingestion, Foundry and production certification remain incomplete. See [execution evidence](docs/engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md) for verified scope and deployment status.

## Start here
- [28 Engineering Council skills and G0–G9 operating model](docs/operating-model/README.md)
- [All 59 stories — routed dependencies and skill owners](docs/operating-model/ALL-STORIES-EXECUTION-ROADMAP.md)
- [Source JSON structural audit and unresolved rights/unit gates](docs/operating-model/SIMULATION-DATA-ADMISSION.md)
- [Engineering Council: all 59 stories execution programme](docs/engineering/COUNCIL-EXECUTION-PLAN.md)
- [59 story HLD/LLD work packets](docs/backlog/STORY-WORKPACK-INDEX.md)
- [Azure target HLD](docs/architecture/06-target-azure-hld.md)
- [Football analytics detailed LLD](docs/architecture/07-football-analytics-detailed-lld.md)
- [Simulation JSON onboarding — source present, not yet certified](docs/data/SIMULATION-FEED-CONTRACT.md)
- [Frontend seven-screen requirements](docs/engineering/FRONTEND-SCREEN-CONTRACTS.md)
- [Quality/release gates](docs/engineering/QA-RELEASE-GATES.md)
- [Architecture and C4](docs/architecture/README.md)
- [Architecture decision records](docs/adr/README.md)
- [MVP backlog and gates](docs/backlog/MVP-BACKLOG.md)
- [Infrastructure](infra/README.md)
- [Azure Pipelines](pipelines/README.md)
- [Story execution register](docs/backlog/STORY-EXECUTION.md)
- [Football low-level design](docs/architecture/05-football-lld.md)
- [Consolidated API contract](docs/api/API.md)

## Run the simulated match

Requires .NET 10 and Node 22+. No Azure credentials are required for the offline experience. For PostgreSQL/Redis local services, Docker Desktop with Compose v2, and first-run/teardown instructions, see [local workstation guide](docs/engineering/M02-LOCAL-DEVELOPMENT.md).

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
# In a second terminal
cd frontend
npm ci
npm run dev
```

From the repository root, `./scripts/dev-health.sh http://127.0.0.1:5080` verifies API readiness with the Docker services. `./scripts/dev-api-health.sh` checks API readiness by itself.

Stop services while preserving local data with `./scripts/dev-down.sh`. To delete the local PostgreSQL and Redis data volumes, use `./scripts/dev-reset.sh` and confirm by typing `RESET`.

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

## Engineering Council delivery hold
The 59 stories have **work packets and a dependency-driven plan**, but are **not all implemented**. The owner-supplied `data/simulations/manu-liv-match-dataset.json` arrived on `main` after the initial review (commit `b978254`). It has 2,199 ordered events and event-attached player positions; see the [source profile](docs/data/MANU-LIV-SOURCE-PROFILE.md). The offline API currently uses the separate fictional `synthetic-v2` embedded definition; there is no canonical adapter from the candidate JSON. The owner confirmed synthetic provenance and redistribution permission on 2026-10-08; independent rights/privacy review, coordinate semantics and football realism remain open. Run `python3 scripts/profile_simulation_feed.py data/simulations/manu-liv-match-dataset.json` to reproduce the read-only structural profile. It does not validate football correctness or independently establish rights. **Do not deploy to Azure DEV without asking and obtaining explicit approval for the exact proposed changes.**

### Source and release truth (Council update)
The file `data/simulations/manu-liv-match-dataset.json` is committed and structurally profiled (2,199 records), but **not** imported into the offline match API or independently approved for rights/privacy or football quality. The owner confirmed synthetic provenance and redistribution permission on 2026-10-08. Unitless coordinate values, player names, source realism and biometrics-like attributes still require review. The separate fictional `synthetic-v2` demo is defined in versioned JSON; its generator does not embed fixture-specific names, chronology or tracking overrides in production C#, and it is not an adapter for the candidate feed. See the role skill index and Story Execution Routing for implementation owners. **Always ask the repository owner to approve the exact DEV change before deploying; PR merge or pipeline resume is not consent.**
