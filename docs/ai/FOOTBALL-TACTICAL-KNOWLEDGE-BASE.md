# Football tactical knowledge base v1

## Purpose and boundaries

This knowledge base gives PitchMind's explanation model a consistent football vocabulary and a disciplined way to interpret evidence. It is organized around repeatable coaching concepts, not personal impersonations. The owner-provided list of coaches spans different eras, competitions and tactical approaches; this document does not claim personal experience working with them, attribute a specific view to each person, or imitate anyone's voice. It synthesizes common coaching and analysis concepts into evidence-checkable cards.

The concise, machine-readable cards in [`football-tactical-knowledge-base.v1.json`](../../src/PitchMind.AgentWorker/Agents/football-tactical-knowledge-base.v1.json) are embedded in AgentWorker. Each of the six specialist profiles plus the evidence-first default references only the relevant cards. The selected profile's principles and cards are placed in one bounded model request. The model still gets a selected-time evidence packet, returns an untrusted candidate, and passes through the existing deterministic verification boundary.

At runtime, the model receives a bounded `promptSummary` for the selected cards; the detailed mechanisms, evidence requirements, analysis steps and limitations remain in the versioned knowledge-base resource and this full guide. This resource explains concepts; it is not a source of match facts, player facts, validated metrics, referee decisions, training data, or an approved tactical detector. A phrase in the knowledge base cannot make a missing observation true.

## A common analysis method

Analyze football at three connected levels:

1. **Team:** phase, organization, spacing, field occupation and response to the opponent.
2. **Unit:** a line or local group working around the ball, including numerical relations such as 2v1 or 3v2.
3. **Player:** an observed action and the player's relationship to teammates, opponents, the ball and direction of play.

Move from broad context to a local mechanism and then to an evidenced outcome. FIFA's coaching analysis framework uses team, unit and player scales and illustrates how roles and numerical relationships change during an attack; PitchMind uses that as a vocabulary aid, not as evidence about any PitchMind match ([FIFA: Scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php)).

For each explanation, follow this chain:

`match/time/revision → phase → observed positions/events → tactical mechanism → opponent response → outcome → uncertainty`

- Pin all statements to the packet's `matchId`, `canonicalRevision`, projection revision, selected time and sequence watermark.
- Separate **observed** source facts from **derived** measurements, **detector classifications**, and **interpretation**.
- Use actual evidence IDs for factual claims. Use later evidence only if it is within the selected-time packet.
- Prefer a small, precise claim over a complete-sounding story. If two mechanisms fit, name the ambiguity.
- Keep counterfactuals conditional: “If the wide defender steps to the ball, the pass may release the runner” is a tactical option, not proof that this happened or would have succeeded.

## Shared vocabulary, metric definitions and evidence confidence

Use one explicit vocabulary across articles, APIs, detectors and model prompts. Similar terms from different feeds must not be treated as equivalent until their definitions are compared. Distinguish possession from phase, pressure from a press, a local overload from a whole-team numerical advantage, progression from penetration, and a recorded event from an inferred tactical mechanism. FIFA's football-language work describes a unified analysis vocabulary and notes that provider definitions and calculations can differ ([FIFA: The Football Language](https://www.fifatrainingcentre.com/en/game/performance-analysis/football-language-analysis/the-fifa-football-language.php)).

Every metric entry should define its name and purpose, numerator, denominator, unit, aggregation window, population/player set, provider, version, missing-data policy and known limitations. Keep totals, per-match rates, per-90 rates, per-ball-in-play rates, completion rates, proportions, averages, medians, percentiles and standard deviations distinct. A rate is not comparable if its denominator, phase, role, competition, minutes or source coverage differs ([FIFA: Data analysis definitions](https://www.fifatrainingcentre.com/en/resources-tools/football-language/data-analysis-definitions.php)).

For each explanation, separate **observed source fact**, **derived measurement**, **detector classification**, and **tactical interpretation**. Name evidence IDs/time windows for factual claims. State relevant coverage, sample size and calibration; provide alternatives when evidence supports them. Use confidence tied to evidence quality, not model fluency. Abstain or use less specific wording when missingness, clock semantics, mapping or coordinate calibration can change the conclusion. A source article supports general football concepts; it never supports a claim about the selected match.

### Reusable metric record

For each metric document: `metricId`, definition, formula, unit, denominator, team/player scope, phase and time window, source/provider, version, minimum coverage, missingness behavior, intended comparisons and prohibited interpretations. For example, “pressures per 30 minutes out of possession” requires a validated pressure-event definition and out-of-possession denominator. It cannot be substituted with raw pressure count or interpreted as pressure quality without outcomes and context.

### Scenario and counterexample record

For each model-evaluable concept, maintain paired, fictionalized scenarios with (1) evidence that satisfies the operational definition, (2) a near-miss that looks similar but lacks one required element, (3) missing or conflicting data, (4) the bounded explanation expected in each case, (5) evidence citations and temporal cutoff, and (6) a reviewer note on plausible alternatives. Store scenario evidence separately from canonical match data and label synthetic examples. Include counterexamples such as a lateral pass mislabeled as a switch, pressure mislabeled as a team press, a high shot count mislabeled as good chance quality, and distance mislabeled as fatigue. A polished answer must not score better than a correct abstention.

| Concept | Supporting scenario | Near-miss/counterexample | Expected bounded response |
| --- | --- | --- | --- |
| In-game adaptation | Same phase repeated: opponent changes the press; team repeatedly changes build-up role; changed exit is observed. | Shape differs after a substitution or score change, but no opponent response/adjustment sequence is documented. | Describe the observed sequence and alternatives; do not assign coaching intent or causal success. |
| Defensive line | Multiple aligned defenders step as the ball travels; runner, cover and keeper are visible. | One defender steps in a single frame, or a later offside flag appears without relevant positions. | State line action only when measured; do not call an offside trap from the later event alone. |
| Scanning/receiving | Video coding records a head turn before receipt and the subsequent touch/action. | Pass completion exists but video/scan/body orientation is unavailable. | Describe scan only in the first case; otherwise report the pass/action and mark perception unknown. |
| Chance context | Shot event plus valid location, pressure, goalkeeper/defender positions and a calibrated model result. | Goal or miss exists without model features, or shot count is used as quality. | Separate shot and result; withhold chance-quality or xG conclusion without validated inputs. |

## 1. Movement dynamics across the field

### Phases to identify

Classify the best-supported phase before naming a tactic:

- **In-possession organization:** build-up, progression through/around/over a unit, creation in the final third, box occupation, restart attack.
- **Out-of-possession organization:** high press, mid-block, low block, retreat/recovery, defending a restart.
- **Transition to attack:** immediate action after a verified regain; direct counter or retained/circulated possession.
- **Transition to defend:** immediate action after a verified loss; counterpress, delay, retreat, or recovery shape.

Possession, phase and shape are not interchangeable. A team may keep the ball without progressing, press without using a high block, or occupy a low block while remaining compact. FIFA's out-of-possession analysis describes a block's width, vertical spacing, collective shifting and pressure as separate principles ([FIFA: Team organisation out of possession](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php)).

### Movement relationships to inspect

When tracking is valid and sufficiently complete, examine:

- **Width and depth:** lateral spread and threat behind/through the opponent. Keep measured team spread distinct from a player's nominal position.
- **Team length and line distances:** space between defensive, midfield and forward units. A team can narrow horizontally, compress vertically, or do both.
- **Centroid and ball-relative location:** where the observed group is centered relative to the ball and pitch. A centroid alone does not reveal shape or intent.
- **Ball-side shift and weak side:** whether multiple players move across together and what space opens away from the ball. A single player moving toward the ball is not a collective shift.
- **Between-lines occupation:** a player or unit positioned in a gap between opponent units. This requires time-aligned opponent and receiver locations, not only a successful pass.
- **Overload and isolation:** a local numerical or positional advantage in a defined area. State the count, participants, area and time.
- **Rest defence:** attacking-team players positioned to protect against a counter while teammates attack. Identify the players/space behind the ball; possession share is not evidence of rest defence.
- **Recovery runs and transition protection:** observed movement to delay the counter, recover a line, protect central access or regain the ball.
- **Opponent response:** the opponent may shift, drop, engage, hold, cover, press, retreat, or expose a lane. Report only responses visible in source evidence.

### Spatial measurement requirements

Before making a spatial or movement claim, validate all of the following:

1. Pitch dimensions and the source's coordinate units.
2. Coordinate origin, axes, orientation and team-relative attack direction for each period.
3. Player and team identity mappings, goalkeeper/outfielder treatment, and active roster at the selected time.
4. Frame timestamps, event clock alignment, sample cadence and missingness.
5. The definition/version of any derived zone, line, compactness, overload or distance threshold.

If any item is unknown, step down in specificity. Say “the sequence progressed into the wide area” only if the event classification supports that; do not say “the winger moved 12 metres into the left half-space” from uncalibrated coordinates. A snapshot can describe a snapshot. A movement trend needs multiple aligned observations. Sparse frames cannot establish acceleration or velocity.

### How to describe dynamics clearly

Use a compact sequence:

> “As [observed ball/event], [identified unit/players] [observed movement]. That created/closed [space or option] according to [derived evidence]. The opponent [observed response]. The next evidenced action was [outcome]. [Limit].”

Do not fill bracketed parts from football convention when the packet is silent.

## 2. Substitutions and the late-match impact question

### What a substitution does and does not prove

A canonical substitution proves that an identified player left and another entered at a recorded time, provided the source event and roster mapping are valid. It does **not** prove why the change was made, whether the outgoing player was tired or injured, what instruction was given, or whether the formation changed. Official match laws define the substitution procedure; tactical purpose remains a separate question ([IFAB Law 3: The Players](https://www.theifab.com/laws/2025-26/the-players/)).

### Tactical mechanisms to consider as hypotheses

A change in personnel can potentially alter:

- **Role and lane coverage:** a like-for-like role, a player taking another line/zone, or different cover of an opponent.
- **Width and depth:** a runner stretching the back line, a wide outlet, a player between lines or an additional box target.
- **Pressing and recovery:** pressure on the first pass, cover behind the presser, sprint/recovery capacity, or the ability to hold a block. Do not infer fitness from name/age or substitute status.
- **Progression and retention:** carrying, passing range, aerial outlet, ability to receive under pressure or protect possession. Require player/event evidence; a profile or reputation is insufficient.
- **Transition balance:** more forward runners can add threat while reducing protection behind the ball; both sides of the trade-off require evidence.
- **Set-piece roles and defensive matchups:** only if the relevant restart and assignments are observed.
- **Game-state risk:** a team may attack more directly, retain the ball, protect a lead or seek a goal, but motive is not established by the substitution itself.

### A defensible pre/post assessment

1. **Pin the change:** outgoing/incoming identities, team, period, clock semantics, event ID and active roster before/after.
2. **Set the observation window:** for example, comparable possession phases or a bounded elapsed-time window before and after. Explain why the window is appropriate. Stop at selected time.
3. **Describe role evidence:** validated role/position data or tracking shows where the incoming player actually operated. Without this, report only the personnel change.
4. **Compare the same measures:** same definitions and coverage before/after for team width/length, line spacing, regain location, progression, pressing sequences or shot/entry outcomes.
5. **Control context:** score, opponent adjustment, dismissal, other substitutions, restart-heavy periods, possession share and sample size can all explain a change.
6. **State what happened, not why:** “After the substitution, the team used more evidenced forward passes in this window” is an association. “The substitute caused the improvement” requires a separate causal design and cannot be inferred from a short before/after sequence.

For a “last-minute substitution,” first define the match clock and period. Stoppage time is part of the period's allowance and provider feeds may encode it differently; never assume a label such as `90+4` means four minutes remain. IFAB Law 7 describes allowance for lost time, including substitutions and disciplinary sanctions ([IFAB Law 7: Duration of the match](https://www.theifab.com/laws/latest/the-duration-of-the-match/)).

### Safe explanation pattern

> “The substitution is recorded at [validated time]. In the observed window afterward, [measured team behavior] changed/did not change. This is consistent with [bounded tactical possibility], but the packet does not establish the instruction or cause.”

## 3. Attacking 2v1 scenarios

### Definition and context

A 2v1 is a local, time-specific relationship between two attacking players and one relevant defender in a defined corridor or zone. State whether the ball carrier is included in the two. Check whether another defender is close enough to cover; the local picture can be 2v1 in a lane while the broader defense still has cover. FIFA coaching material illustrates creating and exploiting 2v1s through overlaps, underlaps, movement into pockets and using a spare player ([FIFA: Developing wide attacking patterns](https://www.fifatrainingcentre.com/en/practice/elite-sessions/clubs-and-academies/english-academies/newcastle-u-15-developing-wide-attacking-patterns.php), [FIFA: Creating and exploiting overloads](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/creating-and-exploiting-overloads.php)). These are coaching examples, not proof of an event in a PitchMind match.

### Attacker's decision tree

- **Carry at the defender:** may commit the defender and release the teammate, or may let the defender delay and protect depth. The model can describe the option only when carrier, defender and teammate positions are observed.
- **Pass to the free player:** depends on lane, timing, weight, receiver orientation and the defender's commitment. A completed pass does not prove that the lane was open earlier.
- **Use a third player:** a wall pass, set, or third-player run can move the ball around the defender. The event sequence must include the intermediate player and the third action.
- **Overlap:** a teammate runs outside/beyond the ball carrier; this can stretch or pin a defender and create a crossing/carrying lane.
- **Underlap:** a teammate runs inside the carrier, often toward an inside channel; direction and destination need tracking or event evidence.
- **Recycle or protect:** if the defender holds shape and cover closes the direct option, the evidence may support retention or return rather than forced penetration.

### Defender's decision tree

The defender may engage the carrier, delay, protect depth, screen a pass, show the attack toward a sideline, or wait for covering support. Determine what happened from position/event evidence. A defender who delays may have prevented penetration even if the attackers retain possession; a defender who engages may expose a pass but can also disrupt the attack. Do not label either “wrong” from outcome alone.

### Analysis checklist

- Identify the corridor, timestamp, ball, attackers, primary defender and covering defenders.
- Confirm team/player mappings, validated coordinates, attack direction and frame cadence.
- Describe the initial numerical relation and whether it changes as players move.
- Record the ball carrier's action, defender response, support movement and next action.
- State the outcome—progression, cross, shot, retained possession, turnover, foul or unknown—only when present.
- Keep the tactical trade-off and alternative response visible; avoid retrospective certainty such as “he should have passed.”

Without validated tracking or a canonical local-overload detector, event-only data cannot prove a 2v1, defender commitment, open passing lane, overlap/underlap, or available option.

## 4. Last-minute fouls and tactical-foul analysis

### Separate match fact from tactical interpretation

Represent these as distinct facts or stages:

1. The source's foul/physical-offence event and its ID/time.
2. Whether play stopped or advantage was applied, as recorded by an authoritative event source.
3. Restart type and location, if validated.
4. Card/caution/dismissal event, if present.
5. Subsequent restart, attack, shot or other outcome.
6. Tactical interpretation: whether the foul interrupted a transition, protected a zone, conceded a restart or may have managed tempo.

IFAB Law 12 governs fouls and misconduct, while Law 7 governs allowance for time lost. These laws are the authority for rules; the analytics model should not adjudicate referee correctness ([IFAB Law 12: Fouls and misconduct](https://www.theifab.com/laws/latest/fouls-and-misconduct/), [IFAB Law 7](https://www.theifab.com/laws/latest/the-duration-of-the-match/)).

### When “tactical foul” may be used

Use this as a cautious interpretation only if evidence shows the defending team was facing an opponent transition and the foul interrupted that progression. Record the phase, location, participants and restart/card aftermath if known. Even then, use “the foul stopped/interrupted the observed transition” rather than “the coach ordered a tactical foul.” A late foul is not automatically cynical, deliberate time-wasting, instructed or strategically successful.

### Last-minute timing

- “Last minute” needs an explicit definition: final 60 seconds of regulation, final 60 seconds of the period including added time, or another competition-specific window.
- Preserve provider time and period labels. Do not convert `45+N` or `90+N` into wall-clock remaining time without validated feed semantics.
- Score, card state, restart position and time all influence the possible tactical consequences, but must be pinned to the selected-time packet.
- Explain actual consequences: free kick/penalty/restart, card, dismissal, attack stopped, continued advantage or lost time. Do not calculate unvalidated danger or claim the event changed the result.
- Never advise, praise or normalize a player deliberately fouling an opponent. Describe the recorded event and its football consequences neutrally.

### Safe explanation pattern

> “At the source-recorded time, [team/player if verified] committed a foul during [supported phase]. Play restarted with [recorded decision]; [card/next action] is/is not in the selected evidence. This interrupted [observed progression] if the event sequence establishes it. Intent and referee correctness are not determined here.”

## 5. Pressing and defensive movement

Use the chain **trigger → pressure → cover/support → opponent response → regain or escape → next action**. A possible trigger (backward pass, poor control, wide reception, receiver facing their own goal) is a hypothesis until the ordered events and/or tracking show it. A trap requires an evidenced route constraint and opponent response; a touchline reception alone is not a trap.

For collective shape, inspect horizontal compactness, vertical distances between lines, ball-side shift, space behind the last line and access through the center. These are separate measurements. A low block can be compact; a high line can be stretched. Pressure by one player is not a collective press. A regain after pressure is temporal sequence, not proof of causation.

FIFA's out-of-possession coaching material describes narrowing horizontally, compressing vertically, moving across as a unit, keeping shape and applying pressure as related but distinct ideas ([FIFA: Team organisation out of possession](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php)). Require validated tracking and coverage before using those descriptions for PitchMind data.

### Pressing triggers and routes out

Potential triggers include a backward or square pass, a receiver's poor or bouncing touch, a pass toward the touchline, an opponent receiving with a closed body orientation, a slow aerial ball or an isolated receiver. These are candidate cues, not universal rules. Name a trigger only if event/tracking evidence records it. Analyse the full chain: cue, first presser's angle and speed, cover shadow or screened lane, supporting players stepping, opponent exits, response and whether the press regained or merely delayed play. One sprint toward the ball is pressure, not proof of a coordinated press. A trap requires evidence that exits were constrained and the opponent responded.

When describing how a team beat pressure, consider playing between pressers, around the press into a free player, over it to a target/space, using a bounce or third-player action, carrying through a vacated lane, switching to the weak side, or retaining/recycling until a safe route appears. FIFA describes passing through/around pressure and high-press organization, traps and risks; these examples inform vocabulary but do not prove PitchMind match events ([FIFA: Passing the press](https://www.fifatrainingcentre.com/en/game/game-analysis/transition-to-attacking/passing-the-press.php), [FIFA: High press](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/high-press.php)).

State the trade-off for either side: bypassing a line can create an advantage if possession is retained but expose a central turnover; pressing high can shorten the route to goal but expose space behind the back line if pressure or cover is bypassed. A successful pass alone does not prove the receiving lane was open before the pass, and a regain after pressure does not prove pressure caused it.

## 6. Transitions as a separate analysis layer

Treat transition as a short, distinct interval following a validated regain or loss, with a declared start event and end condition. Preserve source event ID, sequence and period/clock semantics. Analyse initial reaction, nearest pressure, cover/screen, forward or safe outlet, opponent recovery, next possession phase and outcome. FIFA distinguishes the in-possession and out-of-possession phases and treats the reaction immediately after loss/regain as tactically significant ([FIFA: Defensive transitions](https://www.fifatrainingcentre.com/en/practice/elite-sessions/transition-to-defending/defensive-transitions.php), [FIFA: Core principles of play](https://www.fifatrainingcentre.com/en/game/game-analysis/landing-page-core-principles.php)).

After a regain, distinguish an immediate counterattack from secure possession followed by an organized attack. A counterattack claim needs evidence of rapid forward exploitation while the opponent is disorganized; a long pass or later goal alone is insufficient. After a loss, distinguish counterpressing from delaying the ball and recovering shape. If first pressure is bypassed, inspect recovery runs and protection of central access. Do not infer an instruction or intent from the sequence. Compare alternatives and state if the feed is too sparse to identify the transition.

## 7. Chance creation and prevention patterns

Describe attacking patterns as observable combinations rather than labels inferred from outcomes. Candidate patterns include third-player combinations, wall passes, overlaps, underlaps, switches of play, runs behind a high line, rotations to create a free player, cutbacks, crosses, central combinations and attacks on second balls. Record the sequence, players and lanes only when identity/event data or calibrated tracking supports them. A third-player combination requires an evidenced intermediate action; a switch requires a change of side; a run behind requires a time-aligned starting line and movement; a cutback requires a backward/diagonal delivery from the byline area under a documented definition. FIFA's core-principles series covers third-player combinations and switching play among its in-possession topics ([FIFA: Core principles of play](https://www.fifatrainingcentre.com/en/game/game-analysis/landing-page-core-principles.php)).

Pair each pattern with possible defensive responses: screen the central lane, pass runners between defenders, engage or delay the carrier, protect depth, shift the block, cover the far-post/cutback zone, or contest first/second contact. These are options, not prescribed assignments. Assess the actual defending sequence and outcome. A shot or goal does not by itself establish that a pattern was well executed; a blocked or failed attempt does not prove the decision was poor. State whether evidence describes the pattern, a generated chance, or only the final event.

## 8. Physical and tactical context

Physical measures can describe demands occurring within a tactical phase, but should be contextualized by role, phase, possession state, time on pitch, match period and source coverage. Keep external measures (distance, speed bands, accelerations/decelerations) separate from internal response measures (such as heart-rate or perceived-load data), with units and collection methods explicit. Compare only compatible definitions and samples; normalize denominators where appropriate and preserve raw values and denominators. FIFA contextualized physical metrics using phase-of-play information and notes limitations of measurement methods, particularly for explosive, dynamic actions ([FIFA: Physical-analysis background and method](https://www.fifatrainingcentre.com/en/fwc2022/physical-analysis/background-and-method.php), [FIFA: Monitoring training load](https://www.fifatrainingcentre.com/en/environment/science-explained/high-performance/performance-support/kobe-houtmeyers-on-monitoring-training-load.php)).

Do not infer fatigue, fitness, effort, motivation, injury, decision quality or tactical intent from sprint count, distance, substitution, age or a single time window. A high running total may reflect role and game state; a low count may reflect possession, coverage, minutes, tracking gaps or role. Present physical and tactical observations side by side and use cautious association language. Physiological or training-load data requires its own validated source, privacy controls and specialist review.

## 9. In-game adaptation and opponent response

Separate a team's broad playing principles from match-specific choices and from an observed in-game adjustment. Use the sequence **initial approach → opponent response → observed adjustment → new space/constraint → second response → next outcome**. Compare like phases and account for score, personnel, dismissal, restart, field zone and sample size. A tactical change may address one exposed space while opening another; an apparent response could also reflect a phase or personnel change. Do not infer coaching intent solely from changed positions or possession share. FIFA describes the consistency/adaptability trade-off and illustrates tactical adjustments during a match ([FIFA: Consistent vs adaptable approaches](https://www.fifatrainingcentre.com/en/environment/fifa-research/high-performance/possession-analysis/part-2-2--consistent-v--adaptable-approaches.php), [FIFA: Japan v Sweden, adapting attacking strategies mid-game](https://www.fifatrainingcentre.com/en/fifa-world-cup-2026/japan-v-sweden-adapting-attacking-strategies-mid-game.php)). These sources inform concepts only; do not transfer their team-specific findings to PitchMind matches.

## 10. Defensive line management

Describe the defensive line as a time-varying unit: height relative to the goal, distance to the midfield/forward lines, movement when the ball travels, depth coverage, and relationship to the goalkeeper. Distinguish stepping up to compress space from dropping to protect depth. A coordinated step may compress a between-lines space or attempt to catch runners offside, but mistiming can expose a run behind; dropping may protect depth while creating space in front. These are conditional trade-offs, not quality verdicts. FIFA defines defensive line height and team length as separate metrics ([FIFA: Defensive line height and team length](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--defensive-line-height-and-team-length.php)).

For a line movement claim, require time-aligned locations for the relevant defenders, ball, attackers/runners and goalkeeper, calibrated pitch direction, period/time and adequate frame coverage. Offside status requires the law-defined positions at the relevant instant and suitable authoritative event/geometry; do not infer a successful offside trap from an opponent being flagged later. For a ball in behind, inspect passer pressure, line movement, runner start/timing, cover defender, keeper depth and actual reception/shot sequence. A through pass alone does not prove the line was high, disorganized or deliberately beaten ([FIFA: Balls in behind the defence](https://www.fifatrainingcentre.com/en/game/game-insights/balls-in-behind-the-defence.php)).

## 11. Scanning and receiving under pressure

Scanning is a player's observable head movement away from the ball to gather information; a scan count does not reveal what information was perceived or how it affected a decision. FIFA's youth research reports relationships between pressing, scanning behavior and some subsequent actions in the specific studied sample, not universal causal rules ([FIFA: Scanning activity in elite youth football players](https://www.fifatrainingcentre.com/en/environment/research-brief/high-performance/play/aksum-scanning.php)).

To analyze receiving, separate the approach/pass, pressure at arrival, observable scan if captured, body orientation, first touch, next action and outcome. Suitable video/tracking and a defined coding protocol are needed for scan/body-orientation claims; event-only feeds may show only the pass and next action. Compare role, phase, age/competition context, pressure and sample before interpreting behavior. Do not infer awareness, intelligence, composure or decision quality from scan frequency or one successful forward pass.

## 12. Finishing and chance context

Separate a possession's creation pattern, shot opportunity, shot characteristics and result. For a shot, the relevant context may include location, angle, body part, preceding action, defender pressure/position, goalkeeper position, shot type and whether a block or deflection occurred. Require valid fields and definitions; body orientation, pressure and goalkeeper position need time-aligned video/tracking. A goal does not establish that the chance was high quality, and a miss does not establish poor choice or execution without appropriate context.

Expected-goals or other chance-quality measures must come from a versioned, validated model with documented inputs, calibration population, coverage and uncertainty. Do not create or infer a numerical chance score from tactical prose. Keep chance-quality output as a derived model result, separate from canonical shot events and analyst interpretation.

## 13. Explanations the agents may produce

The agents should explain:

- What the selected evidence shows.
- Which tactical mechanism is consistent with that evidence.
- How the opponent responded, if that response is observed.
- What happened next within selected time.
- What essential context is missing.

The agents should not produce:

- Unsupported lineup, role, formation, score, player-stat, injury, fatigue, intent, emotion or coaching-instruction claims.
- Spatial measurements while coordinate/time semantics are unverified.
- Causal claims from a short event sequence or substitution window.
- Referee-law conclusions or predictions without the appropriate authoritative data/model.
- A tactic presented as the only correct decision when alternatives fit the evidence.

## 14. Evaluation cases for later independent review

1. **Movement:** mirror the pitch for opposite attack directions; omit frames; shift only one player; vary line spacing; validate that the agent limits language to actual coverage.
2. **Substitution:** like-for-like and role-changing examples; substitution followed by no change; multiple simultaneous context changes; selection time before and after substitution; verify no causal claim is emitted.
3. **2v1:** two attackers plus one defender with and without cover; attacker carries/passes/holds; ambiguous sparse frames; verify the profile refuses unsupported numerical advantage and decision judgments.
4. **Late foul:** foul during a verified transition and foul during settled possession; advantage/card/restart present or absent; period boundary and added-time clock; verify event facts stay separate from intent.
5. **Prompt attack:** evidence text instructs the model to invent coach intent, player identity, future outcome or a spatial unit; verify source text stays data and every result remains a candidate until the independent verifier approves it.
6. **Metric vocabulary:** vary denominators, player minutes, event definitions and missingness; verify the model refuses invalid comparisons and preserves metric provenance.
7. **Pressing and exits:** include coordinated press, isolated pressure, failed cover, press bypass through/around/over, and sparse tracking; verify traps, triggers and causal regains are not asserted without evidence.
8. **Transitions:** test regain/loss start boundaries, counterattack versus secure possession, counterpress versus recovery, and selected-time cutoffs; verify order and phase are preserved.
9. **Chance patterns:** include and omit third-player actions, switches, runs behind, cutbacks and defensive cover; verify each label is used only when its defining sequence is present.
10. **Physical context:** vary time played, phase, denominators, tracking coverage and explosive-action metrics; verify fatigue, effort and fitness are not inferred from volume alone.
11. **In-game adaptation:** contrast a repeated adjustment with a score/phase-driven shape change and a coincidental change; require opponent response, alternatives and an outcome cutoff.
12. **Defensive line:** test coordinated step, uncoordinated step, drop to protect depth, goalkeeper cover and through-ball sequences; do not infer line height or offside from one event.
13. **Scanning/receiving:** vary video visibility, scan coding, pressure and first-touch outcome; prohibit claims about perceived information or awareness.
14. **Finishing context:** vary shot location/angle/pressure/keeper fields and model validity; prohibit chance-quality or xG claims when required inputs/model provenance are absent.
15. **Counterexamples:** use near-misses for every tactical label, contradictory sources and partial coverage; reward a precise limitation/abstention over an unsupported complete story.

These are future evaluation requirements, not claims that tests have been run.

## Source and rights note

The sources below are official football coaching or law resources used to define general vocabulary. Their match examples do not become PitchMind match evidence. The knowledge base contains original concise paraphrases rather than copied training drills or source text. The approved candidate feed's synthetic provenance does not resolve its own coordinate, clock, metric or mapping semantics; canonical admission remains blocked pending that review.

### Reference sources

- [FIFA Training Centre — Scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php)
- [FIFA Training Centre — Team organisation out of possession](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php)
- [FIFA Training Centre — Developing wide attacking patterns](https://www.fifatrainingcentre.com/en/practice/elite-sessions/clubs-and-academies/english-academies/newcastle-u-15-developing-wide-attacking-patterns.php)
- [FIFA Training Centre — Creating and exploiting overloads](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/creating-and-exploiting-overloads.php)
- [FIFA Training Centre — The FIFA Football Language](https://www.fifatrainingcentre.com/en/game/performance-analysis/football-language-analysis/the-fifa-football-language.php)
- [FIFA Training Centre — Data analysis definitions](https://www.fifatrainingcentre.com/en/resources-tools/football-language/data-analysis-definitions.php)
- [FIFA Training Centre — Core principles of play](https://www.fifatrainingcentre.com/en/game/game-analysis/landing-page-core-principles.php)
- [FIFA Training Centre — High press](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/high-press.php)
- [FIFA Training Centre — Passing the press](https://www.fifatrainingcentre.com/en/game/game-analysis/transition-to-attacking/passing-the-press.php)
- [FIFA Training Centre — Defensive transitions](https://www.fifatrainingcentre.com/en/practice/elite-sessions/transition-to-defending/defensive-transitions.php)
- [FIFA Training Centre — Physical-analysis background and method](https://www.fifatrainingcentre.com/en/fwc2022/physical-analysis/background-and-method.php)
- [FIFA Training Centre — Monitoring training load](https://www.fifatrainingcentre.com/en/environment/science-explained/high-performance/performance-support/kobe-houtmeyers-on-monitoring-training-load.php)
- [FIFA Training Centre — Consistent vs adaptable approaches](https://www.fifatrainingcentre.com/en/environment/fifa-research/high-performance/possession-analysis/part-2-2--consistent-v--adaptable-approaches.php)
- [FIFA Training Centre — Japan v Sweden: adapting attacking strategies mid-game](https://www.fifatrainingcentre.com/en/fifa-world-cup-2026/japan-v-sweden-adapting-attacking-strategies-mid-game.php)
- [FIFA Training Centre — Defensive line height and team length](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--defensive-line-height-and-team-length.php)
- [FIFA Training Centre — Balls in behind the defence](https://www.fifatrainingcentre.com/en/game/game-insights/balls-in-behind-the-defence.php)
- [FIFA Training Centre — Scanning activity in elite youth football players](https://www.fifatrainingcentre.com/en/environment/research-brief/high-performance/play/aksum-scanning.php)
- [IFAB — Law 3: The Players](https://www.theifab.com/laws/2025-26/the-players/)
- [IFAB — Law 7: The Duration of the Match](https://www.theifab.com/laws/latest/the-duration-of-the-match/)
- [IFAB — Law 12: Fouls and Misconduct](https://www.theifab.com/laws/latest/fouls-and-misconduct/)


## Extended playbooks and source catalogue

Detailed formation matchups, phase-by-phase options, player-role complementarity, goalkeeper integration, set-piece responsibilities, and late-match states are documented in [Football tactical playbooks](FOOTBALL-TACTICAL-PLAYBOOKS.md). FIFA references and review metadata are recorded in the [structured FIFA article catalogue](fifa-training-centre-article-catalog.v1.json), with the scope and topic map in [FIFA Training Centre knowledge index](FIFA-TRAINING-CENTRE-KNOWLEDGE-INDEX.md). These are tactical knowledge references, not match facts or detector outputs.
