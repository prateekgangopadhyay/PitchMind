# ADR-015 — Automatic DEV application delivery from protected main

**Status:** Accepted by repository owner for implementation; external Azure DevOps/GitHub setup remains unverified.  
**Date:** 2026-10-17

## Context
The previous delivery policy paused each DEV application release for an additional manual approval after pull request merge. The repository owner has directed that commits to `main` automatically deploy to Azure. Azure resource provisioning and deployments are separately restricted, so this decision is limited to application releases through the reviewed pipeline.

## Decision
- A PR targeting `main` or `codex/*` runs validation only; it does not publish an image or deploy.
- A commit to owner-protected `main` runs required validation. On success, Azure DevOps validates release identity and the fixed DEV target, builds/pushes an immutable image, deploys that digest to the existing DEV Container App and runs API smoke checks.
- The approved PR merge to protected `main` is the owner authorization for the automatic application release. No manual pause follows the merge.
- Image push and app deployment use separate workload-federated identities with least privilege. The `pitchmind-dev` environment must not add a manual approval check if delivery is expected to remain automatic.
- Infrastructure provisioning, RBAC/identity changes, schema migrations and all STAGE/PROD releases remain separately approved and are not authorized by this ADR.
- If validation, release identity preflight, deployment, or smoke checks fail, downstream stages stop and the failure is surfaced; rollback uses a prior immutable digest under a separately reviewed recovery procedure.

## Consequences and prerequisites
This decision cannot work safely without GitHub protection requiring owner-reviewed PRs, mandatory validation checks, restricted direct pushes and no bypass. The Azure DevOps pipeline must be registered, two service connections configured and scoped, and the DEV environment set up without a manual approval pause. The repository currently cannot verify those external settings. Until verified, source configuration represents intended automation, not an active pipeline or a deployed application.

The existing build and deploy templates publish a digest manifest and deploy by digest. This ADR does not authorize Bicep deployment, new Azure resources, role changes, or a DEV deployment in this change.

## Evidence status
- Pipeline source now expresses main-only auto-delivery after validation: **implemented in this branch; not yet executed in Azure DevOps**.
- Service connection federation, Azure RBAC, GitHub branch protection and Environment checks: **unverified external prerequisites**.
- Azure DEV deployment: **not performed**.
