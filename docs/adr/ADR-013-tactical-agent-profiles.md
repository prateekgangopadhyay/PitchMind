# ADR-013 — Evidence-bounded tactical agent profiles

**Status:** Proposed local implementation; independent football/security review and Product Owner acceptance pending. 2026-10-17.

## Context

PitchMind's current explanation workflow has one bounded model candidate request between validation of a selected-time evidence packet and deterministic verification. Tactical requests benefit from specialist analytical lenses, but adding autonomous agents, free-form tools or independent evidence retrieval would enlarge the trust boundary and conflict with EP08's bounded handoff design.

## Decision

Represent tactical specialists as a versioned, embedded read-only profile catalog and football knowledge base in AgentWorker. Each request selects exactly one profile ID and loads only the knowledge cards referenced by that profile. Profiles include a named tactical school, tactical principles, diagnostic questions, required evidence, blind spots and prohibited inferences. The catalog has six tactical specialists—positional-play architect, high-press architect, defensive-structure analyst, transition strategist, set-piece strategist and match-state strategist—plus the evidence-first default. The 24 knowledge cards cover team movement and spatial evidence; attacking 2v1s; substitutions and late fouls; progression, final-third attack and transitions/rest defence; set pieces and game-state risk; pressing/block dynamics; phase-dependent formations and combinations; matchup adjustments and phase playbooks; player-role fit and goalkeeper integration; late/unusual states; shared vocabulary, metric definitions and evidence confidence; pressing triggers and escape routes; transitions as a separate analysis layer; chance-creation/prevention patterns; physical/tactical context; opponent response and in-game adaptation; defensive-line behavior; scanning/receiving; and finishing context. The written knowledge guide also maintains paired positive, near-miss, missing-data and counterexample scenarios for later evaluation.

Profiles are analytical lenses, not replicas of named real coaches. They do not create domain facts, access tools, retrieve new evidence, write canonical data or bypass the existing verifier. The default profile is `evidence-coach`; unknown IDs are rejected. Workflow traces include the selected profile ID and a version increment. API and public match contracts do not change.

## Consequences

- Tactical language can be specialized while all profiles share one evidence packet, timestamp, revision, provenance and verifier.
- The catalog is data, not executable code; each profile prompt is size-bounded and loaded from an embedded versioned resource.
- Local catalog availability is verifiable from the build, but tactical effectiveness is not established by configuration alone.
- Evaluation must compare each specialist against the evidence-coach baseline, inspect citation and unsupported-claim behavior, and receive independent football review.
- No live Foundry behavior, Azure identity, model performance or Product Owner acceptance is implied.

## Rejected alternatives

- **Multiple autonomous agents per explanation:** adds model calls and handoff complexity without evidence that it improves tactical accuracy; conflicts with current bounded orchestration and cost controls.
- **Separate retrieval tools per coach:** would multiply authorization and prompt-injection boundaries. Evidence remains curated by the existing read-only context builder.
- **Persona imitation of named managers:** risks implying a real person's endorsement or intent. Profiles model tactical disciplines instead.

## Review gates

Before product acceptance, run independent football review of each dossier, test fabricated event/player/score/intent claims and missing tracking/clock data, compare lenses to the default baseline, and prove only verifier-safe output can leave the workflow. The existing synthetic candidate feed remains blocked from canonical admission until its semantics are approved.
