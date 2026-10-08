# Deployment, environments and trust boundaries

```mermaid
flowchart TB
  User[Public Browser] --> FD[Front Door Premium + WAF - optional early MVP]
  FD --> App[Azure Container Apps - API]
  subgraph VNet["Azure VNet and isolated subnets"]
    App --> Pg[(PostgreSQL private access)]
    App --> R[(Managed Redis private endpoint)]
    App --> SB[Service Bus private endpoint]
    Worker[Container Apps Workers] --> EH[Event Hubs private endpoint]
    Fn[Functions VNet integration] --> SB
  end
  App --> Obs[Monitor / Application Insights]
  Worker --> Obs
```

The drawing is logical: subnet delegation, private endpoints, private DNS, egress, Function plan compatibility and regional support must be validated per resource. No claim that this topology has been deployed.

## Environment strategy
- Local: Docker PostgreSQL/Redis and explicit local event/AI adapters.
- DEV: Azure-only integration environment; smallest viable SKUs.
- STAGE: production-like security, test fixtures, deployment and rollback exercises.
- PROD: approved identity, network, backup, SLO, alerts and release gates.
- Separate environment parameter files; secrets and credentials never committed.
- Advanced ADX, ML, AI Search, APIM and Data Lake disabled until a feature gate justifies them.
