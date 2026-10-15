# EP06-S05 opt-in OTLP metrics export — 2026-10-15

**Base:** `3d21bd646c6ffcf0c706938cba6efa29526827dc`.
**Tested implementation commit:** `8e3473416c8371bdab5bdaa732e418db271cd90e`.
**Scope:** Optional export of existing low-cardinality API and PostgreSQL outbox meters through OTLP. The exporter is disabled unless an explicit standard OTLP endpoint environment variable is set. No API route, metric data model, database schema, Docker service, cloud resource, or deployment configuration was added.

## Change

- Registers OpenTelemetry's OTLP metrics exporter only when `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` or `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured.
- Exports the existing `PitchMind.Api` request/error/duration meter and `PitchMind.Infrastructure.Outbox` publication/retry/dead-letter/message-age meter.
- Rejects malformed endpoints, embedded credentials/query/fragment, and HTTP outside the explicit Development/local profile.
- Updated the API registry and C4 component view. OTLP endpoint destination remains deployment configuration; no collector or Azure endpoint was configured.

## Verification

| Check | Result |
|---|---|
| Focused `ApiReadinessAndTelemetryTests` | 9 passed, 1 skipped, 0 failed. The skipped readiness test requires the opt-in PostgreSQL integration runner. |
| `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` | 492 passed, 29 skipped, 0 failed. Skips were 28 opt-in PostgreSQL infrastructure tests and the API readiness test requiring an isolated database. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Passed, 0 warnings, 0 errors. |
| `python3 scripts/api-contract-integration.py` | Passed health/fail-closed route checks, Analyst validation, media/body-size limits, and rate limiting. |
| `node scripts/validate_operating_model.mjs` and `git diff --check` | Passed. |

The tests prove endpoint configuration is opt-in, HTTPS is required outside Development/local, and existing API HTTP behavior is unchanged with no endpoint configured. **No OTLP collector was running and no exported payload was observed.** The full suite skipped database-backed tests; a PostgreSQL integration run was not repeated on this candidate.

## Remaining EP06-S05 gaps

Outbox message age is dispatch age, not browser receipt lag. This change adds no client reconnect/freshness signal, no collector/backend configuration, dashboard, measured latency/availability target, authenticated browser journey, hosted CI evidence, or Product Owner acceptance. EP06-S05 remains unverified/unaccepted. No Azure operation occurred.

## Design references

- [OpenTelemetry .NET exporter guidance](https://opentelemetry.io/docs/languages/dotnet/exporters/)
- [OpenTelemetry.Exporter.OpenTelemetryProtocol 1.19.1](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol/1.19.1)
