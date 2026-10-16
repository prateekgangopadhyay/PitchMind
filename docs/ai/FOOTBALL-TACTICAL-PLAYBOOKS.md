# Football tactical playbooks and evidence framework

## Purpose

This companion expands PitchMind's tactical knowledge into matchup, phase, role, goalkeeper, restart, and game-state playbooks. It combines original, concise football explanations with linked FIFA references. It does not mirror FIFA articles, certify the synthetic match feed, detect tactics by itself, or provide match facts. A playbook is a set of hypotheses to test against a selected evidence packet.

For every match explanation, preserve the distinction between source observation, derived metric/detector, and interpretation. Cite actual selected event/frame IDs. State phase, time window, player coverage, uncertainty, alternatives, and trade-offs. Unknown lineup, coordinates, player identity, match-clock semantics, or opponent locations must remain unknown.

## 1. Formation matchups and in-game adjustments

### Analysis method

1. Establish the authoritative starting lineup and notation convention if naming a formation.
2. Identify the actual phase: build-up, settled possession, high/mid/low block, transition, or restart.
3. Compare occupied lines and local units, not only nominal formation digits. Include the opponent's press/block, ball zone, score, personnel and active players.
4. Look for a local numerical relation and available cover: who is free, who can step, who can recover, and what space is left behind or weak side.
5. Describe an adjustment only after repeated observations show it (e.g. a winger drops, a full-back stays, a pivot steps, a forward screens or the press changes direction).
6. State the possible benefit and matching cost together; show opponent response and outcome.

### Matchup hypotheses to test

| Starting matchup | Possible problem/advantage | Responses to test | Cost or counter-response |
| --- | --- | --- | --- |
| 4-3-3 vs 4-4-2 | Three central midfielders may create a spare against two central midfielders; two forwards may press/pin a back four. | Use the 8s/pivot to find a free central receiver; use a forward to screen the opposing pivot; have a wide midfielder tuck or press the full-back. | A 4-4-2 forward may screen the pivot; wide midfielders can protect full-backs; a central overload can expose wide switches or space behind midfield. |
| 4-2-3-1 vs 4-3-3 | The 10 may receive between a midfield pivot and back line; the opponent's three midfielders may outnumber the double pivot. | A winger can drop to restore midfield numbers; one pivot can step while the other screens; the 10 can move across the opponent's pivot. | Dropping the winger reduces forward outlet; stepping a pivot opens central space; the opponent can press the full-back or screen the 10. |
| Back three vs front two | Three first-line players may create an extra build-up option against two forwards. | A midfielder can jump to the spare centre-back; a wing-back may be pressed; the ball can be circulated to draw/shift the block. | The extra player may be neutralized by cover-shadow/press orientation; high wing-backs can leave channels behind for wide counters. |
| Back three vs front three | Wide centre-backs and wing-backs can match width and create a safe first line, while the front three can press outside-in. | A pivot can drop, a centre-back can carry, or the team can use the goalkeeper to create another angle. | If a wide centre-back steps, a forward can run into the channel; a two-player midfield can be overloaded centrally. |
| 4-2-2-2 vs back three | Two forwards may engage centre-backs while narrow attacking midfielders block central links. | Full-backs can provide width; one attacking midfielder can move outside to support pressure; a pivot can switch play. | Wing-backs may be free if the narrow players stay central; advancing full-backs leave wide transition space. |
| 4-1-4-1 vs double pivot | A single holding player can screen the centre and a midfield line can compress around the ball. | A forward can screen one pivot while an 8 jumps to the other; a winger can narrow or press wide depending on the trigger. | The press can be played around/over; the lone striker may be isolated if the midfield line remains deep. |

These are not universal rankings. A nominal 4-3-3 can defend as a 4-1-4-1, attack in a 3-2-5, or show another shape; only tracking/lineup evidence can establish what occurred. Such labels are summaries, not full explanations. FIFA's [team-shape metric](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--team-shape.php) distinguishes structures in and out of possession.

## 2. Phase-by-phase playbooks

FIFA's [phases-of-play framework](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--phases-of-play.php) breaks ball-in-play time into phases; [scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php) connects team, unit, and player interactions. PitchMind should use a phase label as context, then explain the specific action sequence.

### Build-up against a high press

**Potential plan:** goalkeeper/centre-backs form the first line; pivots and 8s stagger between/around pressing players; wide players stretch the press. Options may include a third-player bounce, carry by a free defender, switching to weak side, or playing directly to a forward and competing for the second ball.

**Potential advantage:** extra passing angles or a free player can help progress through/around/over pressure.

**Trade-off:** a turnover may happen near goal while players are separated; a direct pass can lose possession if the receiver/support/second-ball structure is absent.

**Evidence:** press positions and triggers, goalkeeper/defender/pivot locations, pass sequence, receiver/support, opponent response, next possession. FIFA discusses goalkeeper support in [building from the back](https://www.fifatrainingcentre.com/en/practice/elite-sessions/goalkeeper/goalkeepersbuildup.php) and examples of [passing the press](https://www.fifatrainingcentre.com/en/game/game-analysis/transition-to-attacking/passing-the-press.php).

### Progression through a midfield block

**Potential plan:** move the block with circulation; stretch width/depth; find a between-lines receiver; use a wall pass, third player, carry or switch. A wide unit can create overlap/underlap choices and a local 2v1.

**Potential advantage:** a line can be displaced, opening a lane or free receiver.

**Trade-off:** slow circulation may leave the block intact; an attempted line break can expose a counter if support/rest defence is poor.

**Evidence:** ball and player locations across multiple frames, pass/carry crossing a defined line, receiver action, opponent shift and next event.

### Defending the box

**Potential plan:** pressure the ball source; protect central goal-facing space; track/pass on runners according to observed organization; contest the delivery and first contact; cover cut-back, far-post and edge zones; then attack the second ball or organize a counter.

**Potential advantage:** protect high-value central areas while retaining enough cover for rebound/second phase.

**Trade-off:** a line that collapses toward the goal may expose the edge; stepping to the ball may open a cut-back/far-post lane. Do not describe marking assignments unless observed.

**Evidence:** delivery location/time, all visible attackers/defenders, keeper location/action, first contact, clearance, second contact and outcome.

### Counterattack after a regain

**Potential plan:** first secure the ball; play forward if a safe receiver/lane is present; use a runner to stretch depth, a wide outlet for width, and a second wave. If the direct route is closed, retain and rebuild.

**Potential advantage:** use opponent disorganization before it reforms.

**Trade-off:** forcing a pass can lose the ball again; sending too many runners can remove rest defence.

**Evidence:** canonical regain, player locations at regain, forward/side/backward action, receiver support, opponent recovery and sequence outcome. FIFA's [transition-to-attack session](https://www.fifatrainingcentre.com/en/practice/elite-sessions/transition-to-attacking/transition-into-attack.php) describes forward reactions as coaching options, not as a universal instruction.

### Counterpress after a loss

**Potential plan:** nearest player pressures the ball; teammates cover, screen central/outlet passes or mark nearby receivers; far side narrows. If the opponent escapes, recover toward goal and restore block shape.

**Potential advantage:** restrict the first pass or regain close to the loss.

**Trade-off:** failed pressure can leave space behind or a free weak-side receiver.

**Evidence:** time/location of loss, pressure distance/trajectory, teammate cover, opponent exit/action, regain or escape, later defensive line. Event-only data cannot prove synchronized counterpressing.

## 3. Player-role fit and complementary skills

A role describes a task in a phase, not a permanent identity or a player rating. Use known and validated metrics only; never infer skill/fitness from reputation or position label.

| Role/task | Helpful combination to examine | Potential advantage | Support/risk to inspect |
| --- | --- | --- | --- |
| Pivot | Centre-backs stagger to open a lane; 8/10 provides a forward option; far pivot/full-back protects after the pass. | Connect first line to midfield or switch away from pressure. | If screened, build-up may go around; if pivot advances, a teammate must screen the centre. |
| Advancing full-back | Winger holds width or moves inside; 8 offers a bounce/underlap; centre-back/pivot covers the vacated channel. | Create a wide 2v1, an inside lane, or a different cross/cut-back angle. | Three players can crowd one lane; losing the ball can expose the channel behind. |
| Winger | Full-back chooses overlap/underlap/stay; 8 supports or attacks the half-space; striker occupies centre-backs/depth. | Stretch the block or create a 1v1/2v1 decision. | If the winger narrows, width must come from elsewhere; if isolated wide, the opponent can double up. |
| Pressing forward | Partner screens a pivot or covers a centre-back; midfield steps behind; defensive line compresses space. | Constrain a build-up exit or force play toward cover. | An unsupported sprint can be played around/through; a high line needs depth protection. |
| Target/central striker | One forward pins/threatens depth; a 10/8 runs beyond or collects a set; winger attacks second ball/cross. | Retain direct play or create different runs around a centre-back. | If support is late, the striker is isolated; direct possession may be lost with no second ball. |

Assess the actual task and support. Body orientation, press resistance, aerial ability, speed, stamina, preferred foot and technical quality require validated observations/metrics and enough sample context.

## 4. Goalkeeper integration

- **Build-up outlet:** location should create a visible passing angle and useful distance from pressure; centre-backs/pivots need staggered options. The keeper may help create an extra player, but only if actually involved in the sequence.
- **Against pressure:** record whether the keeper receives, switches, plays through/around/over or is forced long, then trace the next possession. A completed pass alone does not prove the press was beaten.
- **High defensive line:** assess keeper depth in relation to ball, last line, opponent runners and available through-ball space; never infer a “sweeper keeper” role from a reputation.
- **Defensive transition/1v1:** evaluate ball control, attacker angle/distance, defender recovery and keeper's observed movement. FIFA describes a [read-close-set-wait-attack framework for U-20 World Cup goalkeeper 1v1s](https://www.fifatrainingcentre.com/en/game/tournaments/fu20wwc/2026/goalkeeper-1v1s-the-read-close-set-wait-attack-framework.php); apply the source's competition context and do not grade other players from the article.
- **Restart/transition:** after catch/save/restart, inspect distribution choice, receiver availability and opponent recovery. Communication requires a data source that captures it; positional data alone cannot establish speech.

## 5. Set-piece patterns and responsibilities

### Attacking corner

Catalog delivery side/foot/trajectory only if present; identify short option, edge player, runner starting positions and actual movement (near post, central, far post, delayed, screen/block, second ball). Then record first contact, clearance, shot/recycle and transition cover. FIFA's articles include [freeing the edge for a shot](https://www.fifatrainingcentre.com/en/game/game-analysis/set-plays/corners/attacking-corners.php) and [the train and blocker](https://www.fifatrainingcentre.com/en/game/game-analysis/set-plays/corners/the-train-and-blocker.php); these are examples, not universal recommendations.

### Defending corner

Represent zonal, player-to-player or hybrid positions only when visible/known. Track ball watching/sight lines only if source evidence supports it; record first-contact zone, keeper action, short-corner response, edge protection, clearance and counter outlet. FIFA compares [zonal and player-to-player corner organization](https://www.fifatrainingcentre.com/en/game/game-analysis/set-plays/corners/defending-corners-zonal-or-player-to-player.php).

### Free kicks, throw-ins, goal kicks and second phases

For free kicks, separate direct shot, cross, decoy/run, wall, keeper setup, short option, clearance and second ball; legality/referee correctness comes from authoritative law/decision data, not model judgement. For throw-ins, capture location, quick/retained/long option, near support, receiver, pressure and second phase; FIFA's [long-throw-in trend analysis](https://www.fifatrainingcentre.com/en/game/tournaments/fu17wc/2025/part-3-player-development-trends.php) is specific to the U-17 World Cup 2025 context. For goal kicks, trace goalkeeper/defender/pivot/forward options and opponent press. After first contact or clearance, treat the second ball and transition as a distinct sequence. Always inspect who protects the counter when players commit forward.

## 6. Late-match and unusual game states

### State variables

Use canonical score, period, source clock semantics, remaining/added time only if validated, active player count, dismissals, substitutions, restart location, possession/phase, cards, and opponent adjustments. Record unknowns explicitly. A `90+N` provider timestamp is not itself a reliable remaining-time calculation.

### Scenario questions

- **Protecting a lead:** did the block actually become deeper/narrower? Were safe outlets retained? Did opponent pressure/box occupation increase? Did a substitution precede a measurable role change?
- **Chasing a goal:** did the team add box runners, raise the line, use wider/faster restarts or leave more counter space? What coverage remained behind the attack?
- **After a dismissal:** which line/role changed, where was numerical cover lost/added, and how did the opponent respond? Red-card status does not automatically establish a specific formation.
- **After a late substitution:** define equivalent windows; separate first-action settling from a later stable sample; record other simultaneous state changes. Say “followed by” unless causal analysis justifies “caused.”
- **Restart-heavy closing minutes:** analyze the restart sequence, lost-time clock, cards/fouls and next possession separately from open-play trends. A late foul does not automatically mean deliberate time-wasting or tactical intent.

Risk is multi-dimensional: central protection, depth behind the last line, wide channels, counterpress/recovery, box occupation, set-piece cover and outlet retention. Avoid simplistic claims such as “they shut up shop” unless each observable part is supported. Use current [IFAB Laws](https://www.theifab.com/laws/latest/) for restart/substitution/time/foul rules, not tactical inference.

## 7. Knowledge article catalogue and review workflow

The versioned source register is [`fifa-training-centre-article-catalog.v1.json`](fifa-training-centre-article-catalog.v1.json). Each reviewed entry records a stable source ID, title, direct link, topics, football code, age/competition context, source type, known publication date, review date, short original abstract and rights/use note. A null publication date means the article page did not expose a reliable date during review; do not guess it.

The catalogue is curated, not exhaustive. To add a source, verify the direct page, capture those metadata fields, write a short original paraphrase, distinguish a practice-session example from tournament analysis/metrics/interview/terminology, note age and competition scope, and state what match evidence would be necessary to apply it. Do not bulk scrape, reproduce copyrighted prose/media, or treat article examples as PitchMind match evidence. Review links and metadata periodically because FIFA's library changes.

## Model application contract

Every playbook should be represented as: **phase/state → participants/roles → trigger → mechanism → possible advantage → trade-off/failure mode → required evidence → alternatives → source**. The AI must separate source facts, derived metrics, detector outputs and tactical interpretations. It must abstain from unsupported lineup, role, intent, fitness, spatial, referee or causal claims. This guide provides vocabulary and hypotheses; deterministic canonical evidence and verification remain the authority for match facts.
