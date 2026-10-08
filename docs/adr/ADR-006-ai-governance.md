# ADR-006 — Evidence-grounded agentic AI
Status: Accepted (target), 2026-10-08

**Decision:** Agents consume read-only, least-privilege evidence tools. Foundry generates candidate language; claim validator gates publication. Enforce execution states, call/token/time/cost/concurrency budgets, trace provenance and deterministic degraded fallback.

**Consequences:** Red-team tests and golden claims are release gates. AI failure cannot block deterministic match truth or live event processing.
