# PitchMind

Evidence-first football intelligence for match exploration and analysis. The application includes a DEV candidate-live path for visualizing source events; it does not treat the unvalidated feed as canonical football facts.

## Architecture at a glance

The C4 views below describe the **Azure DEV application path implemented in the repository**. They use Azure services only. The release template declares these resources, identities and settings, but the diagrams do not prove that every resource, network route, identity grant or runtime connection is healthy. Owner-supplied DEV screenshots show the candidate UI rendering source events and positions; see the [DEV integration status](docs/engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md) for the evidence limits. LOCAL Docker topology is documented in the [local development guide](docs/engineering/M02-LOCAL-DEVELOPMENT.md).

### C4 Level 1 — System context

```mermaid
flowchart LR
  User["Fan / analyst"] --> Web["PitchMind React UI"]
  Web -->|"bearer access token"| API["PitchMind API — Azure Container Apps"]
  Web <-->|"interactive sign-in / token"| Entra["Microsoft Entra ID"]
  Source["Synthetic candidate JSON packaged with API image"] --> API
  API --> EH["Azure Event Hubs"]
  EH --> Worker["PitchMind EventWorker — Azure Container Apps"]
  Worker --> PG[("Azure Database for PostgreSQL Flexible Server")]
  PG --> Dispatch["Candidate outbox dispatcher"]
  Dispatch --> Redis[("Azure Managed Redis — cursor cache")]
  Dispatch --> SignalR["Azure SignalR Service"]
  SignalR -. "cursor notification" .-> Web
  Web -->|"selected-match event pages"| API
  API -. "bounded evidence hypothesis; runtime call unverified" .-> Foundry["Microsoft Foundry project agents"]
```

### C4 Level 2 — Azure DEV containers and data stores

```mermaid
flowchart LR
  Browser["React UI"] --> API["ASP.NET Core API — Azure Container Apps"]
  API -->|"source-order candidate events"| EH[("Azure Event Hubs")]
  EH --> Worker["EventWorker — Azure Container Apps"]
  Worker -->|"durable stage before checkpoint"| Outbox[("Azure PostgreSQL — candidate outbox")]
  Outbox --> Dispatch["API outbox dispatcher"]
  Dispatch -->|"event history"| PostgreSQL[("Azure PostgreSQL — candidate history")]
  Dispatch -->|"latest cursor"| Redis[("Azure Managed Redis — rebuildable cache")]
  Dispatch -->|"cursor only"| SignalR["Azure SignalR Service"]
  SignalR -. "refresh notification" .-> Browser
  PostgreSQL -->|"ordered event pages"| API
  API -->|"match data"| Browser
```

### C4 Level 3 — Azure DEV application components

```mermaid
flowchart LR
  subgraph Simulation["Simulation service"]
    JSON["Candidate JSON reader"] --> Publisher["Source-order event publisher"]
  end
  Publisher --> Broker[("Azure Event Hubs")]
  Broker --> Consumer["EventWorker consumer"]
  Consumer --> Stage["PostgreSQL candidate outbox"]
  Stage --> Dispatcher["Outbox dispatcher"]
  Dispatcher --> History["Candidate event history"]
  Dispatcher --> Cursor["Redis cursor projection"]
  Dispatcher --> Notifier["SignalR cursor publisher"]
  History --> ReadAPI["Candidate page API"]
  ReadAPI --> UI["React match view"]
  Notifier -. "cursor; no match facts" .-> UI
  Canonical["Canonical BFF read ports"] --> CanonicalPG[("PostgreSQL canonical projections")]
  UI --> Canonical
  Evidence["Selected-time evidence"] -. "read-only input" .-> AI["Foundry explanation adapter"]
  AI -. "candidate text; not canonical facts" .-> UI
```

### C4 Level 4 — Azure DEV deployment boundary

```mermaid
flowchart TB
  Browser["Browser"] --> ACAIngress["Azure Container Apps HTTPS ingress"]
  ACAIngress --> API["PitchMind API + React assets — ACA"]
  API -->|"managed identity"| EH[("Azure Event Hubs")]
  EH --> Worker["PitchMind EventWorker — separate ACA"]
  Worker -->|"managed identity"| Blob[("Azure Blob checkpoint container")]
  Worker -->|"Entra PostgreSQL principal"| PG[("Azure Database for PostgreSQL Flexible Server")]
  API -->|"candidate outbox/history"| PG
  API -->|"Entra data access"| Redis[("Azure Managed Redis")]
  API --> SignalR["Azure SignalR Service"]
  SignalR -. "cursor notification only" .-> Browser
  API -. "configured project-agent request; live call unverified" .-> Foundry["Microsoft Foundry"]
```

### High-level application flow

1. The API-hosted Simulation Service reads the candidate JSON packaged in its image and publishes source-order events to Azure Event Hubs.
2. The separate EventWorker consumes Event Hubs, durably stages each raw envelope or poison record in the PostgreSQL candidate outbox, then advances the Blob checkpoint.
3. The API dispatcher writes candidate event history to PostgreSQL and updates the rebuildable Redis cursor; it publishes a cursor-only Azure SignalR notification after durable processing.
4. The UI reads ordered event pages and player-position fields from the API. SignalR prompts refresh; it does not carry match facts. PostgreSQL remains authoritative for persisted candidate records, which remain non-canonical.
5. The AI Analyst API can send bounded, selected-match source evidence to an allowlisted Foundry project agent and return a labeled hypothesis. Automatic per-event report generation, successful live Foundry calls, and a canonical multi-agent workflow are not established by available evidence.

The DEV release template declares Azure runtime settings, services, identities and roles. The supplied screenshots show the deployed UI rendering candidate events and positions, but do not prove each data-plane connection. Candidate source clock semantics, coordinate units and orientation, player metric definitions, event mappings, and football realism remain unverified; see the [DEV integration status](docs/engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

## Run locally

Follow the [local infrastructure guide](docker/README.md) for the full setup and candidate-live walkthrough. Run each application process in its own terminal:

```bash
# Terminal 1: Docker dependencies and schema (LOCAL only)
./scripts/dev-up.sh
./scripts/dev-migrate.py
# Terminal 2
./scripts/start-local-api.sh
# Terminal 3
./scripts/start-local-eventworker.sh
# Terminal 4
npm --prefix frontend run dev -- --host 127.0.0.1
```

Open the Vite URL printed by the frontend command. LOCAL infrastructure uses Docker PostgreSQL, Redis and Kafka; Azure credentials are not required. DEV, STAGE and PRODUCTION use Azure services only.

## Project references

- [Architecture source documents and C4 details](docs/architecture/README.md)
- [API contract](docs/api/API.md)
- [Local development guide](docs/engineering/M02-LOCAL-DEVELOPMENT.md)
- [Simulation source profile and data-quality limits](docs/data/MANU-LIV-SOURCE-PROFILE.md)
- [Story execution register](docs/backlog/STORY-EXECUTION.md)
- [CI/CD pipelines](pipelines/README.md)
