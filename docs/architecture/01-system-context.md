# C4 Level 1 — PitchMind system context

**Reality labels:** **OBSERVED** = executable offline source path and recorded previous Azure DEV demo. **TARGET** = capabilities to be implemented and certified. The [architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) and [source profile](../data/MANU-LIV-SOURCE-PROFILE.md) identify current blockers. This diagram is not deployment evidence.

```mermaid
flowchart LR
  Fan["Fan / supporter"] --> PM["PitchMind football intelligence"]
  Analyst["Tactical analyst / broadcaster"] --> PM
  Operator["Authorized operator"] --> PM
  JSON["Approved synthetic EPL-context feed (JSON)"] --> PM
  PM -. "TARGET authentication" .-> Entra["Microsoft Entra External ID"]
  PM -. "TARGET grounded explanations" .-> Foundry["Microsoft Foundry"]
  PM -. "TARGET observability" .-> Azure["Azure Monitor"]
```

## Product journey and scope
Discover simulated fixture → time-consistent score, match state and pitch → evidence-supported tactical key moment → **Show me why** → replay cited events/observed frames → grounded fan/analyst explanation with deterministic fallback.

## Trust and provenance boundaries
- The 28.6 MB `data/simulations/manu-liv-match-dataset.json` has been structurally profiled but **not yet rights-certified or canonically imported**. It includes real EPL club/player names and unverified performance/biometric-like attributes; labeling synthetic is mandatory once the owner confirms origin and permission.
- Football truth comes only from validated input and deterministic, versioned derivation; the frontend and model cannot manufacture it.
- The current public read-only offline fixture is distinct from future authenticated multi-user and administrative features. No claim Entra or Foundry is currently active.
- Separate service roles for simulation import, event worker, BFF reads and evidence-only Foundry tools, with least privilege. A PR/diagram does not authorize DEV deployment.
