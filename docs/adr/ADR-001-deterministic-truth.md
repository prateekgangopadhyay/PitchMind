# ADR-001 — Deterministic canonical football truth
Status: Accepted (target), 2026-10-08

**Decision:** Domain algorithms own match state, possession, phase, tactical mechanisms and evidence. AI cannot alter canonical truth. Store versioned input provenance and algorithm version; preserve observed/derived/interpretation/hypothesis distinctions.

**Consequences:** Golden fixture replay tests are mandatory. Event ordering, coordinate normalization and causality claims require explicit evidence. Reject unsupported claims.
