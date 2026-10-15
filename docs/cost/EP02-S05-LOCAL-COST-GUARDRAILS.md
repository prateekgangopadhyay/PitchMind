# EP02-S05 local cost and release guardrails

## Evidence status

This change adds local static guards for the declared DEV release path and wires them into the authored pipeline. It checks that delivery remains opt-in, two dedicated protected Azure DevOps Environment approval stages remain present and ordered, image creation follows the pre-mutation gate, and DEV deployment follows digest approval through the declared `pitchmind-dev` environment. Each stage also keeps a `ManualValidation@0` human pause, explicitly labeled as not proving approver identity. The validator self-test mutates the environment bindings and required pause controls and confirms that unsafe variants fail.

This is partial implementation evidence for EP02-S05. It is not a cost estimate, an active Azure budget or alert, a tested DEV rollback, a deployment, or Product Owner acceptance. The Azure pipeline definitions and environment checks have not been executed or independently observed in Azure during this work.

## Current cost posture

| Area | Observed posture | Cost evidence |
| --- | --- | --- |
| Azure Container Apps | Existing deployment templates declare a workload; live DEV sizing and replica limits are not verified. | Unestimated |
| PostgreSQL | The current infrastructure module is a placeholder; no deployed SKU, storage, HA, backup or retention configuration was verified. | Unestimated |
| Event streaming and jobs | Event Hubs and Service Bus capacity and retention are not approved or measured. | Unestimated |
| Microsoft Foundry | No active model deployment or token workload was verified. | Unestimated |
| Monitoring, storage and network | Ingestion, retention, private endpoints, Front Door and egress have no measured workload basis. | Unestimated |
| Budget alerts | No active subscription or resource-group budget and alert evidence was observed. | Missing |

`docs/cost/COST-ASSUMPTIONS.md` explicitly describes its monthly ranges as illustrative. They must not be represented as a reviewed project estimate.

## Inputs required for an estimate

Before approving a cost ceiling, collect and review:

- sponsor-approved monthly ceiling, alert thresholds, currency and accountable responder;
- target Azure region, expected run duration, active hours and schedule;
- expected matches, event volume, concurrent viewers, API request rate and AI token usage;
- Container Apps sizing, minimum/maximum replicas and scaling limits;
- PostgreSQL SKU, storage growth, high availability, point-in-time recovery and retention;
- Event Hubs throughput/partitions/retention and background-job volume;
- monitoring ingestion and retention, storage, private networking, ingress and egress;
- which optional services are enabled for the approved scenario.

Until those inputs are approved, no numerical estimate or budget threshold is asserted here.

## Release and rollback evidence

The pipeline declares `enableDevDelivery: false`, protected environment bindings named `pitchmind-release-authorization` and `pitchmind-release-digest-authorization`, a main/non-PR build condition, and the `pitchmind-dev` deployment environment. Each approval stage also has a ManualValidation pause as a human checkpoint. The authored pipeline invokes the operating-model validator and its self-tests; the pipeline has not been registered or run in Azure DevOps. The validator checks repository declarations and self-tests unsafe mutations locally. Microsoft documents that `ManualValidation@0` can be resumed or rejected by users with Queue builds permission, so that task is not an identity control ([task reference](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/manual-validation-v0?view=azure-pipelines)). The Environment approval checks are the only intended identity-restricted mechanism and are configured outside YAML.

These are repository-level declarations only. Repository evidence says the pipeline is not registered or executed in Azure DevOps. Before enabling delivery, an Azure DevOps administrator must create both release authorization environments and configure each with the repository owner as the only permitted approver (or an owner-controlled group), restrict environment and bypass-capable administrator permissions to owner-controlled principals, and restrict pipeline use accordingly. Independent read-only evidence must confirm the effective approver list and permissions, any bypass capability, that Azure DevOps is configured to run this pipeline, the service connection is least-privileged, and no alternate release path can write to DEV. An environment name in YAML does not create an approval check; if no check is configured, that job may proceed without owner authorization. The queue-time `enableDevDelivery` opt-in must remain unused until these controls are configured and independently verified. None of those server-side controls are proven by the YAML or local validator. No Azure service connection, environment, resource, revision or image was accessed or changed for this story.

Rollback procedure to validate before any approved DEV release:

1. Record the current deployed revision and immutable image digest.
2. Confirm the previous known-good revision and required database compatibility.
3. Revert the Container App to that revision and verify health, dependency probes and telemetry.
4. Confirm the failed revision receives no traffic and record the recovery evidence.
5. Stop further release activity until the cause and any data migration recovery are reviewed.

This procedure is a plan only; it has not been exercised. EP02-S05 remains unaccepted until cost inputs are approved, active budgets and alerts are demonstrated, workload/resource limits are verified, owner-only protected environment controls are independently observed, and rollback/redeploy evidence is recorded in the permitted environment.
