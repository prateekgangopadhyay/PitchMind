# DEV deployment execution evidence — 2026-10-08

Scope: read-only synthetic offline vertical slice, not completed canonical Azure product. Target confirmed by owner: `rg-pitchmind-dev-001`, centralindia. STAGE/PROD are excluded.

## Preflight

Existing resource inventory: Log Analytics, ACR, Event Hubs, Storage, Key Vault, ACA managed environment, VNet and Service Bus; no workload/DB at baseline. ACR Basic/admin credentials disabled/public access enabled; ACA environment Succeeded. Main and demo Bicep compile passed. Demo what-if succeeded with only API workload, user-assigned identity and registry-scoped AcrPull assignment creates; no deletes/modifications.

Frozen-source SHA-256 over sorted Dockerfile, build props, solution, application/frontend source (excluding generated bin/obj/node_modules/dist): `5cdd03512986e0dfd75cb1beb79d2cb6e6e5d2cf3102b14d3ed00678e7df5391`. Source was uncommitted; this is source content evidence, not a committed release revision.

## Build execution

ACR Tasks denied the build with Azure `TasksOperationsNotAllowed` for this subscription. No ACR build ID/image was created. Fallback: local Docker `--platform linux/amd64`, registry identity login and push. No admin credentials or registry access-policy change.

## Azure DevOps configuration

Confirmed organization `https://dev.azure.com/prateekgangopadhyay`, project `pitchmind` resolves. Read-only service-connection inventory contains one GitHub endpoint; no AzureRM federated connection. Pipeline inventory is empty. Repository pipeline now contains main-only opt-in immutable build → DEV delivery and real PostgreSQL test result gates. Actual CI/CD execution is not proved and remains blocked pending Azure federation/connection provisioning and repository pipeline registration.

## Deployment and smoke

Registry push completed and `az acr repository show` confirms image digest `sha256:a173a40a2f0d3be099af189b5abd1b84b573fe975d6309f7ace64ed9a88f4b19`. `docker manifest inspect` confirms linux/amd64 runtime manifest `sha256:b4ce07b448814d80876ff0acabcd4c7822c0e61f6639ab1689ce7459c81c665f` plus build attestation. Image built once locally and pushed; deployment uses index digest, never mutable tag.

ARM deployment `pitchmind-offline-dev-20261008` completed `Succeeded`. Actual API `pm-dev-api` reports provisioning `Succeeded`, running `Running`, ready revision `pm-dev-api--6exc5id`, with exactly the pushed digest above. The single container creates the API workload and serves the built static UI from the same origin.

Endpoint: [DEV synthetic demo](https://pm-dev-api.whitebay-d0e7e2e6.centralindia.azurecontainerapps.io).

`/usr/bin/python3 scripts/api-smoke.py <endpoint>` passed actual HTTPS checks: historical replay; 55:08→55:09 score boundary; insight suppression before outcome; evidence ID resolution; deterministic fallback; invalid time/audience and unknown match. System curl verified TLS and frontend HTML response over HTTP/2; API smoke checked correlation headers. Initial Framework Python invocation failed its local trust store (`self-signed certificate in certificate chain`); system trust succeeded without TLS bypass.

This proves the bounded read-only DEV prototype runs from the recorded artifact. It does not prove managed PostgreSQL durability, Azure ingestion/SignalR/Foundry, production authentication, measured throughput, rollback/restore or production readiness. No STAGE/PROD mutation occurred.
