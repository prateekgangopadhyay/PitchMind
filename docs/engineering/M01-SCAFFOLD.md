# M01 — Repository Scaffold and Clean Architecture

Status: Implemented on feature branch; build/test verification pending runner execution.

## Project dependency graph
- Domain: no project/package dependencies.
- Contracts: transport DTOs; no Domain dependency.
- Application → Domain.
- Infrastructure → Application (transitively Domain).
- API → Application, Infrastructure, Contracts.
- EventWorker / AgentWorker → Application; infrastructure adapters added in later stories.
- Tests validate domain rules and project dependency direction.

## What M01 intentionally does not include
PostgreSQL canonical adapter, Event Hubs consumer, Azure Foundry integration, SignalR, Docker infrastructure, production authentication, tactical detection or end-to-end match experience. Those belong to M02+.

## Local validation (requires .NET 10 SDK and Node 22+)
```bash
dotnet restore PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
dotnet run --project src/PitchMind.Api
cd frontend && npm install && npm run build && npm test && npm run lint
```

## Acceptance criteria
- [x] Project files and dependency boundaries established.
- [x] API, event worker, agent worker and React shell added.
- [x] Domain, application and architecture tests authored.
- [ ] Clean SDK build and test verified by CI or local execution.
- [ ] Frontend install/build/test/lint verified.
- [ ] PR reviewed and merged.
