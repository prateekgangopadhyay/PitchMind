# PitchMind repository-wide engineering instructions

Operate under [Engineering Council Operating Model](../docs/operating-model/ENGINEERING-OPERATING-MODEL.md); select focused [role skills](skills/README.md) instead of treating every role as a mandatory approver. Start each change from an exact story in docs/backlog/HACKATHON-MVP-EPICS.md and the execution register, not an invented backlog. Read docs/architecture/01-system-context.md through 05-football-lld.md and ADR-001..008 for relevant decisions.

Canonical rules:
- Deterministic football truth stays pure in PitchMind.Domain; AI writes no canonical data.
- One selected elapsed time controls state, events, insight, pitch and explanation; no future leakage.
- Fixed stadium coordinates use metres 105 × 68 and halftime-direction transforms; observed/derived/pattern/interpretation/hypothesis classes never collapse.
- PostgreSQL is canonical; Event Hubs telemetry and Service Bus background commands are distinct; at-least-once, idempotent and replayable; Redis and SignalR noncanonical.
- LOCAL Docker adapters only; DEV/STAGE/PROD Azure-only; GitHub source and Azure DevOps CI/CD.
- Read code and evidence before asserting implemented, tested, deployed or production-ready. The offline synthetic API/DEV demo is not proof of a canonical cloud path.
- Use fictional synthetic demonstration data with traceable provenance and lawful usage. New data/simulations inputs require validation; a filename is not source approval.
- Apply security, accessibility, football review, budget, test and rollback requirements appropriate to the changed surface.
- **Do not deploy stories to DEV without explicit, change-specific repository owner approval.** PR creation/merge is not authorization; STAGE/PROD need separate protected approvals.
- Record status, precise evidence/commit/environment and design changes in the relevant existing docs. Do not invent test results or defer blockers silently.