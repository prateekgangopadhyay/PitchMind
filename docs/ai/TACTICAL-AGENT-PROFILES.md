# PitchMind tactical agent profiles v1

PitchMind now has six named tactical specialist profiles plus one evidence-first default. They are **analysis lenses**, not copies of real people and not autonomous model agents. The names refer to football coaching disciplines; each profile carries its own tactical school, principles, diagnostic questions, evidence requirements, blind spots and prohibited inferences in [`tactical-agents.v1.json`](../../src/PitchMind.AgentWorker/Agents/tactical-agents.v1.json).

The Foundry adapter inserts the selected profile and only its referenced cards from the [Football Tactical Knowledge Base](FOOTBALL-TACTICAL-KNOWLEDGE-BASE.md) into a bounded request. One profile is selected for one model call. The existing workflow still validates a read-only, selected-time MatchIntelligence packet, generates one untrusted candidate, then returns only deterministic verifier output. The profile cannot retrieve data, call tools, revise canonical match state, publish a claim, or override verification. Unknown profile IDs are rejected. Traces carry the profile ID and workflow version.

## Available agents

| ID | Agent | Tactical domain | Main questions |
|---|---|---|---|
| `evidence-coach` | The Evidence Coach (default) | Evidence-first explanation; observed fact vs derived metric vs interpretation | What does the selected insight say? Which evidence supports it? What remains unknown? |
| `positional-play` | The Positional Play Architect | Space occupation, width/depth, support angles, progression, overloads and rest defence | Is the relevant space, line, support or numerical relationship actually evidenced? |
| `high-press` | The High-Press Architect | Press triggers, first/supporting pressure, traps, opponent response, regain and counterpress | What started pressure? What did the opponent do? Was there a regain or escape? |
| `defensive-structure` | The Defensive Structure Analyst | Block shape, compactness, zonal shifting, line height and defensive traps | Are enough time-aligned players and lines visible to describe structure? |
| `transition-counterattack` | The Transition Strategist | Turnover definition, forward exploitation, support runs, numerical advantage and protection | What began the transition, how did it progress, and what outcome is documented? |
| `set-piece-strategist` | The Set-Piece Strategist | Restart classification, delivery, first contact, second ball, marking and counter-protection | Is this restart identified? What is known about the delivery and the next actions? |
| `game-management` | The Match-State Strategist | Score/time state, personnel changes, tempo and conditional risk options | Which score, clock, substitution and sequence evidence is fixed at this selected time? |

## Shared tactical method

Every specialist follows the same evidence order:

1. **Fix context.** Use only the packet's match, canonical revision, selected time, sequence watermark, score, source version and provenance.
2. **Separate truth types.** Label what is directly observed, what a deterministic detector derived, what the detector permits as interpretation, and what is unknown.
3. **Apply one lens.** Ask the specialist's diagnostic questions; do not turn an unavailable observation into an assumed answer.
4. **Cite evidence.** Every factual claim cites evidence IDs from the packet. Claims cannot cite evidence beyond selected time or sequence.
5. **State limits.** Keep uncertainty about source coverage, clock semantics, player mapping and spatial calibration explicit.
6. **Use plain language.** Fan audience gets ordinary language with a brief explanation of necessary terms; analyst audience gets concise tactical terms. Audience changes wording, never facts.
7. **Return a candidate.** The response is untrusted. Independent deterministic verification remains the only path to a user-facing safe explanation.

Metric comparisons must use compatible definitions, units, denominators, time windows, roles and source coverage. Confidence follows evidence quality, calibration, sample size and missingness, not the model's fluency. General FIFA references inform football vocabulary only; selected-match claims still need packet evidence.

## Football domain details encoded in each specialist

### Positional play

The profile distinguishes width from depth; ball-side overload from general possession; a support angle from an assumed passing option; line-breaking progression from a merely forward pass; and rest defence from a high possession share. Third-player combinations require a sequence showing the wall-pass and third-player action. Scanning/body orientation and off-ball runs require suitable time-aligned video/tracking; event-only data cannot establish them or what a player perceived. Shot context and any derived chance-quality score require validated inputs and model provenance.

### High pressing

The profile reasons in the order **trigger → pressure → opponent response → regain or escape → exploitation**. Candidate triggers include a backward pass, poor control, touchline reception, slow aerial reception or a receiver facing their own goal, but the agent may name one only when the observed sequence supports it. A trap additionally requires constrained exits and an evidenced response. The escape may go through, around, over, by carry/third player, switch or recycle, as shown by the ordered sequence. Counterpressing starts after a defined turnover and depends on validated clock semantics. Temporal sequence alone does not prove causality or coaching intent.

### Defensive structure

The profile treats compactness as at least horizontal and vertical, requiring defined player groups, calibrated coordinates and an explicit measure. A block or coordinated shift requires broad time-aligned player coverage across relevant lines. Line height relative to goal, team length and unit spacing are distinct; step/drop/hold claims need ball, runner, cover and goalkeeper context. A touchline location does not prove a pressing trap or offside strategy. If team/player mappings, orientation or frames are missing, the analyst must say shape cannot be determined.

### Transitions

The profile distinguishes defensive-to-attacking transitions after a defined regain from attacking-to-defensive transitions after a defined loss. A counterattack needs evidenced rapid forward exploitation and opponent disorganization; a long pass or later goal alone is insufficient. It separately tracks first action, pressure/cover, outlets, opponent recovery and the phase end condition. Numerical advantage requires correctly mapped attackers and defenders in the relevant area and time. Speed, open space, recovery imbalance, and rest defence require calibrated tracking rather than sparse events.

### Metrics and physical context

Every statistic needs its definition, formula, unit, denominator, scope, time window, provider/version and missingness/coverage. Rates with different denominators are not directly comparable. External running load and internal physiological/perceived responses remain separate measures. Role, phase, possession, minutes and match state provide context; distance or sprint volume alone cannot establish fatigue, effort, fitness, injury, intent or decision quality.

### Chance creation and prevention

Patterns such as third-player combinations, switches, overlaps/underlaps, runs behind, cutbacks and second-ball attacks require their defining ordered events or calibrated off-ball positions. Pair the attacking action with observed defensive cover/response. Keep pattern, chance, shot and result separate; a goal or failed attempt is not by itself a quality judgment.

### Set pieces

The profile classifies the restart before discussing the routine, then separates delivery, first contact, second ball, retained attack/clearance and outcome. Marking scheme, decoy run, blocker, screen or intended target are not inferred from one frame. A set-piece success rate needs a defined sample, restart classification, outcome and denominator; a single restart cannot establish a club's routine or quality.

### Match-state strategy

The profile treats match state as a time-indexed combination of score, period, remaining time, player availability and possession context. It uses only validated fields in the packet. A lead-protection or chase-game explanation is a hypothesis unless an approved policy classifies it; substitutions prove personnel changed, not why or what shape followed. It may describe general conditional options, but cannot claim a manager's intention or prescribe a specific formation or substitution without squad and role context.

## Hard evidence limits

- The Manchester United/Liverpool candidate JSON is not a canonical fact source. Its coordinate units/orientation, clock semantics, player metrics, mappings and football realism remain unresolved. No profile bypasses that block.
- Spatial language such as “within three metres,” “compact,” “overload,” “high line,” “open lane,” “body shape,” “speed” or “numerical advantage” requires validated tracking semantics and coverage.
- A detector classification does not establish intent, quality, success, danger, causation or future outcome.
- A tactical profile cannot add events, players, lineups, substitutions, scores, statistics or player identities absent from the evidence packet.
- The tactical agent cannot browse, use unrestricted tools, call another agent, write PostgreSQL, mutate the event ledger, expose raw candidate output or publish an explanation.
- Full profile prompts are bounded by catalog validation; the Foundry request and output remain bounded by existing request, token, time and concurrency budgets.

## Runtime status and acceptance

This is local code and a versioned prompt catalog. It does not provision Azure resources, add an AgentWorker message consumer, make a live Foundry call, establish the source's football correctness, or pass Product Owner acceptance. Existing EP08-S03 and EP08-S04 gates still apply. Profile value and quality require independent football review, adversarial claim tests, a single-agent baseline comparison and human Product Owner acceptance.
