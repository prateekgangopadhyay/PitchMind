# Engineering Council — operating model

This is the canonical collaboration contract for 28 PitchMind engineering-role skills. Read [visual operating model](WORKFLOW-DIAGRAMS.md), [E2E operating model](ENGINEERING-OPERATING-MODEL.md), [delivery artifact templates](DELIVERY-ARTIFACT-TEMPLATES.md), [architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) and [architecture/ADRs](../architecture/README.md) before assigning implementation work.

The 28 portable GitHub Copilot Agent Skills live in [../../.github/skills](../../.github/skills/README.md). Each is a role-specific capability with triggers, required references, executable review questions, deliverables, stop conditions and handoffs. Skills are on-demand; the always-applicable minimal policy is in [../../.github/copilot-instructions.md](../../.github/copilot-instructions.md).

## Starting an epic or story

1. Quote the exact EPxx-Sxx acceptance criteria from the authoritative hackathon backlog; link the execution register and current code.
2. Ask Product Owner to choose one story DRI, desired user result, P0 boundaries and dependencies.
3. Ask Principal Architect to run the architecture delta/risk route; select only necessary specialist skills.
4. Staff + story DRI produce HLD/LLD, contracts, tests and a partitioned implementation plan.
5. Implement locally on a feature branch; independent reviewers inspect code/tests and collect *observed* evidence.
6. Create a reviewable PR. Merge/release only after the applicable gates and named decision authorities.
7. **Ask the owner before every DEV story deployment.** No deployment/approval inference from earlier prototype activity.

Treat architecture documents as target intent, integration smoke as environment-scoped proof and clean pipeline output as build-scoped proof.