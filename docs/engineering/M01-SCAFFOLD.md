# M01 — Repository Scaffold and Clean Architecture

Status: Feature branch implementation; executable verification pending.

## Architecture
- Domain: zero external NuGet dependencies.
- Contracts: transport DTOs independent of domain.
- Application → Domain; contains ports and use-case validation.
- Infrastructure → Application; contains a local in-memory event store only.
- API → Application, Infrastructure, Contracts; exposes /api/health and /health.
- EventWorker and AgentWorker: minimal host scaffolds, no Azure integration.
- Tests: Domain, Application and architecture dependency direction.

## Deliberate exclusions
Canonical PostgreSQL, Event Hubs, Foundry, SignalR, Docker Compose, authentication, tactical engine, CI/CD integration and real Azure deployment are subsequent backlog items.

## Validation commands (requires .NET 10 and Node 22+)
```bash
dotnet restore PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
dotnet run --project src/PitchMind.Api
cd frontend
npm install
npm run build
npm test
npm run lint
```

## Acceptance
- [x] Clean Architecture project structure and dependency references authored.
- [x] API, workers, frontend and basic tests authored.
- [ ] Clean SDK build and tests verified.
- [ ] Frontend build, tests and lint verified.
- [ ] Pull request reviewed and merged.
