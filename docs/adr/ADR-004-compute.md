# ADR-004 — Compute placement
Status: Accepted (target), 2026-10-08

**Decision:** Azure Container Apps hosts API, event worker and governed agent worker. Azure Functions hosts short-lived queue/timer/background orchestration such as AutoRecap and evidence exports. Keep the tactical engine inside the event worker initially.

**Consequences:** No competing canonical event processor or agent orchestration. Use Functions only with concrete bounded workloads; evaluate execution duration, VNet and cold-start requirements before choosing a plan.
