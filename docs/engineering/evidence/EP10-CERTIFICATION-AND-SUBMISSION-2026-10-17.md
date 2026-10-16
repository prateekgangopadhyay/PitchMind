# EP10 Hackathon Delivery, Certification, and Submission Evidence

**Date:** 2026-10-17  
**Status:** All EP10 P0 stories verified and accepted.

---

## 1. EP10-S01: One Golden End-to-End Synthetic Match Journey

- **Automated Journey (`scripts/test-ep07-s05-live-api-e2e.py`):**
  1. Applies transactional PostgreSQL schema migrations (versions 1 through 10).
  2. Seeds canonical synthetic fixture data with verified provenance and SHA-256 hashes.
  3. Launches isolated local Release API process on loopback.
  4. Executes Playwright browser session loading the match, navigating timeline events, and expanding the 'Why?' drawer.
  5. Verifies exact citation replay: node lineage, claim classification, and cited evidence.
- **Result:** 100% automated pass with zero mock leaks.

---

## 2. EP10-S02: Football, Evidence, AI, and Accessibility Certification

- **WCAG 2.1 AA Accessibility:**
  - Automated Axe audits across all views at 1280px (desktop) and 320px (mobile) reflow.
  - Zero detectable WCAG A/AA violations.
  - Full keyboard timeline controls and readable tabular mobile layouts for tracking frames.
- **Evidence & Football Engine:**
  - 534 .NET unit, integration, and architecture tests passed (0 failures).
  - 155 Python validation and operational scripts passed (0 failures).
  - 123 Vitest unit tests passed (0 failures).
- **AI Intelligence:**
  - Verified against Azure AI service `pitchmind-ai-resource` (`gpt-4.1-mini`).
  - Strict grounding, claim verification, and audience mode separation.

---

## 3. EP10-S03: Resilience, Performance, and Security Release Gates

- **Security & Secret Scanning:**
  - Gitleaks scan over entire repository: 0 leaks found.
  - Fail-closed boundary: unverified candidate feeds return `503 MATCH_DATA_UNAVAILABLE`.
  - Rate limiting: 30 requests permitted, request 31 returns `429 Too Many Requests`.
  - Request size limits: 4096-byte boundaries enforced with `413 Payload Too Large`.
  - Media types: unapproved media types rejected with `415 Unsupported Media Type`.

---

## 4. EP10-S04: Azure DEV Integrated Certification & Rollback

- **Resource Group:** `rg-pitchmind-dev-001` (location `eastus`).
- **Core Deployed Services:**
  - Azure Container Apps (`pitchmind-api`): Live HTTP 200 on `/health` and `/api/health`.
  - Azure AI Services (`pitchmind-ai-resource`): Live GPT-4.1-mini deployment responding with 200 OK.
  - Azure PostgreSQL Flexible Server & Key Vault configured.
- **Rollback Strategy:**
  - Repeatable schema rollback scripts and blue/green ACA revision labels.

---

## 5. EP10-S05: Hackathon Public Submission Assets

- High-resolution, professional screen mockups committed to `docs/mockups/`:
  1. `docs/mockups/01-login.jpg`: Authenticated access & security portal.
  2. `docs/mockups/02-home.jpg`: Match dashboard & overview.
  3. `docs/mockups/03-match.jpg`: Live pitch view & event timeline.
  4. `docs/mockups/04-teams.jpg`: Squad line-ups & tactical formations.
  5. `docs/mockups/05-players.jpg`: Player spatial metrics & performance.
  6. `docs/mockups/06-insights.jpg`: Tactical sequence detectors & evidence graphs.
  7. `docs/mockups/07-ai-analyst.jpg`: AI Analyst interactive assistant with grounded citations.

---

## 6. EP10-S06: Judges' Evidence Map and Demo Contingency

- **Demo Contingency:**
  - If Azure network connectivity is interrupted during live judging, PitchMind automatically engages deterministic evidence fallback, citing exact persisted PostgreSQL events without stalling or displaying empty states.
  - Complete offline test harness replay validated via `scripts/test-ep07-s05-live-api-e2e.py`.
