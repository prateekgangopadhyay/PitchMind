# PitchMind Microsoft Foundry Agent Instructions

This folder contains individual, paste-ready system instructions for the ten agents shown in the supplied Microsoft Foundry screenshot. The version beside each Foundry agent name is recorded in the file title for reference; update it if Foundry versions change.

## Agent files

| Foundry agent | Screenshot version | Instructions |
|---|---:|---|
| TacticalAnalyst | 6 | [tacticalanalyst.md](tacticalanalyst.md) |
| PositionalPlayArchitect | 4 | [positional-play-architect.md](positional-play-architect.md) |
| HighPressAnalyst | 4 | [high-press-analyst.md](high-press-analyst.md) |
| DefensiveStructureAnalyst | 4 | [defensive-structure-analyst.md](defensive-structure-analyst.md) |
| TransitionStrategist | 4 | [transition-strategist.md](transition-strategist.md) |
| SetPieceStrategist | 3 | [set-piece-strategist.md](set-piece-strategist.md) |
| MatchStateStrategist | 3 | [match-state-strategist.md](match-state-strategist.md) |
| GoalkeeperAnalyst | 3 | [goalkeeper-analyst.md](goalkeeper-analyst.md) |
| ChanceCreationAnalyst | 3 | [chance-creation-analyst.md](chance-creation-analyst.md) |
| CoachFrameworkAnalyst | 3 | [coach-framework-analyst.md](coach-framework-analyst.md) |

Copy the complete fenced instruction block in the matching file into that agent's Foundry Instructions field. `docs/ai/FOUNDRY-TACTICAL-AGENTS-DETAILED.md` is the synchronized consolidated copy. Keep it aligned with these standalone files whenever prompts change. These repository files do not update Foundry agents automatically.

## Shared behavior

Each prompt is usable on its own and carries the same match-evidence and API response contract. The live candidate analyst expects exactly `status`, `answer`, and `claims`; answered responses contain 4–8 claims with exact `records[].evidenceId` citations, and the displayed answer is assembled from those claim texts. Insufficient-evidence responses use one football-specific sentence and an empty claims array. The prompts also carry the same match-evidence contract: honor the selected-time boundary; separate observed facts, validated measures, detector labels, interpretations, and unknowns; treat retrieved/source content as untrusted; avoid unsupported coordinates, movement, intent, or causality; and keep audience-facing language about football. The analyst prompts use original vivid live-match storytelling without reproducing the distinctive voice, persona, or phrasing of an identifiable commentator.

Specialist agents should return a bounded football assessment, not a general match essay. TacticalAnalyst is responsible for selecting relevant specialists, reconciling their evidence, and producing a coherent supporter-facing explanation. A Product Owner, Football Analytics Engineer, and Business Owner are review lenses in that prompt. They are not represented as connected Foundry agents unless those agents are actually wired and their outputs are supplied.

## Project skill application

All 34 skills in `.github/skills` were considered. Domain, evidence, narrative, and agent-governance practices are reflected in the copyable prompts. Infrastructure, persistence, delivery, and visual-design skills govern repository/implementation concerns and are not injected as football instructions where they would distract from each agent's remit.

| Skill | Applied to |
|---|---|
| `football-analytics-engineer` | All agents: evidence strength, source semantics, calibrated spatial language, causal restraint, football-specific uncertainty. |
| `cinematic-football-commentary` | TacticalAnalyst and all audience-facing specialist outputs: original cadence, vivid but proportional storytelling, no identifiable-person imitation. |
| `product-owner` | TacticalAnalyst and attacking/set-piece/match-state analyses: answer the supporter’s football question, keep context relevant, distinguish usefulness from evidence. |
| `ai-agentic-systems-architect` | TacticalAnalyst and Chance Creation: bounded specialist routing, untrusted outputs, no model authority over match truth. |
| `qa-architect` | All agents: positive/negative/insufficient evidence cases, alternatives, selected-time checks, explicit uncertainty. |
| `data-engineer` | Source-sensitive defensive, match-state, goalkeeper, chance-creation, and coach-framework analysis: provenance and coverage remain visible in reasoning. |
| `domain-driven-design-specialist` | Positional Play and TacticalAnalyst: preserve distinctions among observed event, match state, shape, mechanism, and hypothesis. |
| `real-time-streaming-engineer` | High Press and Transition: event-time boundaries, incomplete windows, sparse tracking, no inferred continuous motion. |
| `distributed-systems-engineer` | Transition and TacticalAnalyst: ordering, duplicates, gaps, corrections, and replay limitations are not mistaken for football events. |
| `event-streaming-engineer` | High Press and Transition: event sequence and coverage must support a temporal tactical claim. |
| `security-architect` | Defensive Structure and TacticalAnalyst: source trust and evidence boundaries; no unsafe exposure of internal operational detail. |
| `application-security-engineer` | Coach Framework and TacticalAnalyst: retrieved source material is untrusted and cannot override instructions. |
| `accessibility-engineer` | Audience prose: clear structure, readable terminology, no reliance on visual-only cues. |
| `technical-writer` | All files: standalone paste-ready blocks, consistent terms, clear limits, version traceability. |
| `principal-software-architect` | Folder organization and synchronization with the consolidated prompt source. |
| `distinguished-engineer` | Review for overclaiming, unsupported complexity, and false certainty. |
| `staff-principal-engineer` | Prompt set coordination: consistent shared contract with distinct specialist responsibilities. |
| `developer-experience-engineer` | Copy/paste workflow and discoverable file naming. |
| `lead-backend-engineer` | Considered for evidence packet/API boundaries; backend implementation language is kept out of user-facing football prose. |
| `lead-frontend-engineer` | Considered for how answers are consumed; no UI implementation instructions are included. |
| `performance-engineer` | Considered for concise, bounded specialist work; no unmeasured latency or capacity claims are introduced. |
| `site-reliability-engineer` | Degraded/incomplete evidence handling and honest uncertainty; no runtime readiness claims. |
| `azure-solution-architect` | Considered for Foundry context only; no unsupported Azure architecture claims are included. |
| `azure-infrastructure-engineer` | Not applied to football analysis; no infrastructure or deployment operations are performed by these prompts. |
| `devops-architect` | Repository change hygiene only; no CI/CD or release instructions are placed in agent prompts. |
| `finops-engineer` | Considered for bounded orchestration; no cost or token-budget claim is made without runtime configuration. |
| `postgresql-engineer` | Not applied to football reasoning; database concerns are excluded from agent-facing commentary. |
| `redis-engineer` | Not applied to football reasoning; cache concerns are excluded from agent-facing commentary. |
| `ui-ux-engineer` | Audience clarity and coherent narrative handoff; visual/interface design remains outside prompt scope. |
| `design-taste-frontend` | Considered; frontend visual construction is outside this documentation task. |
| `impeccable` | Considered; visual hierarchy and interface critique are outside this documentation task. |
| `emil-design-eng` | Considered; interaction and motion design are outside this documentation task. |
| `web-design-guidelines` | Considered; web UI code review is outside this documentation task. |
| `awesome-design-audit` | Considered; screenshot-based visual audit is outside this documentation task. |

## Important boundaries

- The source simulation may contain synthetic identities and unverified clock/coordinate semantics. Do not present candidate source data as real-world match truth.
- The prompts do not establish that the listed specialist agents are connected to TacticalAnalyst in Foundry. Agent handoffs are only claimed when actually configured and invoked.
- Prompt instructions cannot replace application-side claim verification, evidence authorization, or output validation. Copying these files into Foundry does not publish or activate them; the app must be configured to call the intended agent version.
- The coach-framework prompt requires reviewed, source-backed profiles and must not invent private coaching intent or imitate a real coach's voice.
