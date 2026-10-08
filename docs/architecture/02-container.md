# C4 L2 — Containers

```mermaid
flowchart TB
  Browser[React Client] --> Edge[Front Door / WAF - optional MVP]
  Edge --> API[Container Apps API / BFF]
  API <--> SignalR[Azure SignalR]
  Feed[Telemetry Producer] --> EH[Event Hubs]
  EH --> Worker[Container Apps Event Worker + Tactical Engine]
  Worker --> PG[(PostgreSQL)]
  Worker --> Cache[(Azure Managed Redis)]
  Worker --> Outbox[Transactional Outbox in PostgreSQL]
  Outbox --> Bus[Service Bus]
  Bus --> Agents[Container Apps Agent Worker]
  Bus --> Fn[Azure Functions: AutoRecap / Recovery]
  Agents --> Foundry[Foundry]
  Agents --> PG
  Fn --> Blob[Blob Storage]
  API --> PG
  API --> Cache
  API --> SignalR
  EH -. archival .-> Lake[Data Lake P2]
  Lake -. analytics .-> ADX[Data Explorer P2]
  PG -. indexed projection .-> Search[AI Search P2]
```

## Contracts
- Event Hubs: high-throughput match telemetry; partition key `matchId`.
- Service Bus: durable commands, retries and dead letters.
- PostgreSQL: canonical state, immutable evidence and outbox.
- Redis: rebuildable cache; never canonical truth.
- SignalR: fan-out, not a source of durable events.
- Foundry: only candidate narratives; deterministic fallback required.
- Functions: background jobs, not the core stream processor.

## Executable local slice (2026-10-08)

The implemented local path is browser → API `SyntheticMatchService` → seeded immutable fixture → deterministic projection/evidence/fallback. It does not traverse PostgreSQL, Event Hubs, SignalR or Foundry. The diagram above remains the Azure target; deployability gaps are tracked in [Azure delivery delta](../engineering/AZURE-DELIVERY-GAPS.md). Do not enable a cloud profile by renaming the in-memory adapter.
