# Azure delivery implementation and remaining verification

**Current state:** the repository contains a guarded Azure DEV release path and the API/EventWorker candidate-feed adapters. The current Azure inventory and known runtime issues are maintained in [Azure DEV integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md). This page does not treat a Bicep declaration or historical test result as current cloud verification.

## Implemented in source/configuration

- Azure Container Apps API and EventWorker topology; Azure Event Hubs candidate publisher/consumer.
- PostgreSQL candidate outbox/history persistence and Blob checkpointing.
- Redis cursor projection, Azure SignalR cursor notification, API event-page reads, and local polling/backfill behavior.
- Entra signed-in identity plus a separately controlled DEV guest candidate-feed option.
- Direct candidate analyst API to an allowlisted Foundry project agent with bounded source evidence and response/citation validation.
- Azure DevOps pipeline stages for validation and guarded DEV release.

## Current observed limitations

- The 2026-10-28 inventory reports the current Redis service as a Redis 7 container app on Azure Container Apps (`pm-dev-redis-cache`), not Azure Managed Redis. Its minimum replica is one.
- Recent owner-supplied analyst API calls return `503 ANALYST_UNAVAILABLE`. Successful Foundry project-agent invocation is not currently verified.
- The candidate clear-data operation is implemented to clear PostgreSQL candidate state and Redis, then restart replay at source sequence zero. A recent owner-supplied DEV call returned a dependency-unavailable error; successful end-to-end clear/cache/restart behavior in the deployed environment is not verified.
- Azure resource presence does not prove active revisions, effective identity grants, database readiness, event consumption, checkpoint progression, Redis updates, or SignalR delivery.
- Candidate source records remain non-canonical; source clock, event semantics, coordinate conventions, player metrics, and football realism are not approved as canonical facts.
- The UI's locally computed tactical discussion helper is illustrative and does not prove that Foundry agents generated the displayed text.

## Release controls and required evidence

`pipelines/azure-pipelines.yml` currently defaults `enableDevDelivery=true` and `enableCandidateFeed=true`. Confirm the target parameters, online self-hosted agent, service connections, exact what-if, cost artifact, immutable image digest, and protected Azure DevOps environment checks for each release. The repository cannot verify server-side permissions or checks.

For current release evidence, trace one source sequence across Event Hubs, EventWorker, PostgreSQL outbox/history, Blob checkpoint, Redis cursor, SignalR notification, API page response, and the browser. Verify database migration/readiness and Entra/guest boundaries separately. For AI, retain a successful project-agent request status and response alongside its API correlation ID. Until those probes pass, describe the path as implemented/configured but not fully verified.

## Historical record

The original 2026-10-08 assessment and subsequent prototype history were superseded by the dated inventory and status in [Azure DEV integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md). Earlier test counts and deployment reports remain historical evidence for their exact source revisions only; they are not a current certification or current Azure health signal.
