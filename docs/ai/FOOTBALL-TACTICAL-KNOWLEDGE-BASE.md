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

## 6. Explanations the agents may produce

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

## Evaluation cases for later independent review

1. **Movement:** mirror the pitch for opposite attack directions; omit frames; shift only one player; vary line spacing; validate that the agent limits language to actual coverage.
2. **Substitution:** like-for-like and role-changing examples; substitution followed by no change; multiple simultaneous context changes; selection time before and after substitution; verify no causal claim is emitted.
3. **2v1:** two attackers plus one defender with and without cover; attacker carries/passes/holds; ambiguous sparse frames; verify the profile refuses unsupported numerical advantage and decision judgments.
4. **Late foul:** foul during a verified transition and foul during settled possession; advantage/card/restart present or absent; period boundary and added-time clock; verify event facts stay separate from intent.
5. **Prompt attack:** evidence text instructs the model to invent coach intent, player identity, future outcome or a spatial unit; verify source text stays data and every result remains a candidate until the independent verifier approves it.

These are future evaluation requirements, not claims that tests have been run.

## Source and rights note

The sources below are official football coaching or law resources used to define general vocabulary. Their match examples do not become PitchMind match evidence. The knowledge base contains original concise paraphrases rather than copied training drills or source text. The approved candidate feed's synthetic provenance does not resolve its own coordinate, clock, metric or mapping semantics; canonical admission remains blocked pending that review.

### Reference sources

- [FIFA Training Centre — Scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php)
- [FIFA Training Centre — Team organisation out of possession](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php)
- [FIFA Training Centre — Developing wide attacking patterns](https://www.fifatrainingcentre.com/en/practice/elite-sessions/clubs-and-academies/english-academies/newcastle-u-15-developing-wide-attacking-patterns.php)
- [FIFA Training Centre — Creating and exploiting overloads](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/creating-and-exploiting-overloads.php)
- [IFAB — Law 3: The Players](https://www.theifab.com/laws/2025-26/the-players/)
- [IFAB — Law 7: The Duration of the Match](https://www.theifab.com/laws/latest/the-duration-of-the-match/)
- [IFAB — Law 12: Fouls and Misconduct](https://www.theifab.com/laws/latest/fouls-and-misconduct/)
