# Football domain knowledge atlas

## Purpose and source discipline

This atlas gives PitchMind a broader association-football domain reference for explaining verified match evidence. It combines a curated set of public laws, governing-body coaching and competition analyses, and peer-reviewed research. It is not an exhaustive encyclopedia or a transcription of source pages. The linked sources are paraphrased, and their examples remain source examples rather than facts about a PitchMind match.

The named-coach profile files are analytical lenses built from general tactical concepts. They do not claim that PitchMind authors or agents worked with the named coaches, reproduce their private methods, or imitate their voices. No profile, article, formation convention or model output can supply a missing event, player attribute, instruction, intention or match fact.

### Evidence hierarchy for explanations

1. **Canonical match facts:** validated PitchMind event and tracking records, preserving source IDs, order, provenance, clock and period semantics.
2. **Derived observations:** versioned calculations from validated facts, with formulas, coordinate conventions, coverage and missing-data behavior.
3. **Classifications:** outputs from documented, evaluated detectors with known error bounds.
4. **Tactical interpretations:** conditional explanations that point back to observations and name plausible alternatives.
5. **General knowledge:** this atlas and its sources; it can explain a mechanism but cannot establish that mechanism occurred in the selected match.

When evidence is insufficient, reduce specificity or abstain. Separate association from causation, action from intent, and source examples from the selected match.

## Laws, match format and competition context

The [IFAB Laws of the Game](https://www.theifab.com/laws/latest/) are the authority for association-football laws. Check the edition applicable to the match and any permitted competition modifications. For analysis, Laws 3, 5, 7, 8, 10, 11, 12, 13, 14 and 15 are frequent context for substitutions, referee authority, time allowance, restarts, goals, offside, fouls/cards, free kicks, penalties and throw-ins. The analyst should record what the feed reports and should not adjudicate a referee decision from incomplete event data.

Clock representation is provider-specific. Preserve period, source clock, added-time notation and elapsed-time conversion as distinct fields. A displayed `90+N` does not establish how much time remains. A foul late in a match may interrupt a transition, concede a dangerous restart, stop play, or have another context; “tactical foul” is an interpretation requiring sequence evidence, not a law-defined event label.

State the code and format. Eleven-a-side association football is not interchangeable with futsal, beach soccer, small-sided development formats or adapted youth football. Player count, pitch size, offside application, substitution rules and restart procedures can differ. Do not transfer a formation or metric across codes without a documented mapping.

## The game as interacting phases and scales

Use the phase taxonomy as context rather than as a complete tactical explanation. FIFA's World Cup analysis describes in-possession phases (including build-up, progression, final-third play and counterattack), out-of-possession phases (press/block, counterpress, recovery and defensive transition), and set-play phases ([FIFA phases of play](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--phases-of-play.php)). Analyze the **team**, the relevant **unit**, and the **player** interaction together ([FIFA scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php)).

For each possession sequence, ask:

- What phase is supported by the source, and what event or possession change starts/ends it?
- How are width, depth, line spacing, support, cover and access to the next line represented in data?
- What does the opponent do in response, and which response remains a plausible alternative?
- Does the observed action change territory, create an option, produce a shot/restart, retain possession or lose the ball?
- What is not observable: scan, body shape, communication, assignment, intent, pressure quality or instruction?

### In possession

Common principles include creating length and width, separating opponents, positioning between/around lines, supporting the ball, progressing through/around/over pressure, switching play, combining with a third player, carrying to commit a defender, and occupying the box and rebound areas. These are options, not a mandatory sequence. A safe recycle may be better supported than a forced forward pass when the lane, timing or rest-defence evidence is absent.

UEFA's competition observations show how context changes the solution: the 2022/23 Champions League report discussed a trend toward playing short from goal kicks, while the 2023/24 technical observers highlighted compact defending and space-oriented marking, and UEFA's 2024/25 reports cover wide-area control, goal-kick strategy and competition-specific patterns ([2022/23 report overview](https://www.uefa.com/uefachampionsleague/news/0284-18c4875c5c5b-4bb185955cb9-1000--2022-23-uefa-champions-league-technical-report-overview-buil/), [2023/24 report overview](https://www.uefa.com/uefachampionsleague/news/0290-1bb668a7bfcf-6d7906bc878b-1000--technical-reports-read-the-2023-24-reports-for-the-champions/), [2024/25 technical report catalogue](https://www.uefa.com/uefachampionsleague/news/029d-1ebab9db818d-b34c67444955-1000--now-available-2024-25-club-competition-technical-reports/)). These are season- and competition-bound observations; do not treat an observed trend as a universal prescription.

### Out of possession

Describe a defensive organization through line height, width, depth, compactness, cover, pressure, screening, marking reference and the space the unit is willing to concede. A high press is a collective relation among the first presser, cover, screened passing routes and the rest of the team; one player chasing the ball does not establish a team press. A mid/low block can steer play wide, protect central access, delay an attack or prepare to counter. Each choice exposes something: space behind a high line, central gaps if a midfielder jumps, weak-side access if the ball-side unit over-shifts, or reduced forward outlet if the block retreats.

FIFA's tactical material describes compactness, coordinated shifting, pressure and cover as connected but distinct principles ([FIFA out-of-possession organization](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php)). UEFA's technical observers have also described teams defending space rather than tracking opponents everywhere; marking references must be observed rather than inferred from a shape label.

### Transitions and rest defence

After a verified regain, distinguish an immediate counter from a secure first action and a later settled attack. Check forward lane, ball carrier, runners, support, opponent recovery and second-ball structure. After a verified loss, inspect the closest pressure, cover behind it, central-screening, far-side narrowing and recovery routes. A counterpress can recover the ball or delay progress; if bypassed, it can leave open space behind. FIFA's core-principles series treats third-player combinations, switches, high pressing, counterattacks and counterpressing as separate mechanisms ([FIFA core principles](https://www.fifatrainingcentre.com/en/game/game-analysis/landing-page-core-principles.php)).

Rest defence is the attacking team's protection against a counter while it has the ball. Describe the actual players and spaces behind/beside the attack, not merely possession percentage or a formation name. Consider the trade-off between committing runners to threaten the box and retaining cover for a turnover.

## Formations, roles and matchups

A formation is a lineup-derived starting shorthand. A phase shape is a time- and possession-dependent arrangement. Keep the two distinct. A nominal 4-3-3 may defend in a 4-1-4-1 or attack in a back-three/five-lane structure, but these labels require lineup plus positional evidence and a stated counting convention.

Evaluate matchups by mapping the actual numerical and spatial relations: first line against pressers, midfield access, wide channel, half-space, depth behind the last line, and cover around the ball. For each proposed overload, specify area, time, involved players and nearby cover. Then state the counter-risk. An apparent 3v2 may be neutralized by a cover shadow or a defender stepping out; a wide 2v1 may be less useful if the carrier cannot face forward or the far-side defender can cover. A matchup suggests questions to test; it does not guarantee advantage.

Roles are requirements that can be shared across different player types, not assumptions about individuals. A pivot may need to offer a passing angle, scan, receive under pressure, protect central transitions and vary distribution. An overlapping full-back may need timing, speed, crossing or cutback choices, recovery and a complementary wide player who can move inside or hold width. A winger may stretch depth, isolate a defender, combine or recover into a block. A pressing forward may curve the run, screen a pivot, coordinate triggers and remain available after a regain. A target striker may connect direct play and occupy defenders, but needs support for second balls and protection if isolated. Validate these attributes from approved player evidence; never infer them from a role label or reputation.

For substitutions, compare like-for-like windows and phases before/after the recorded change. Report the player's observed role and team behavior; score, opponent changes, dismissals, restart mix and sample size are competing explanations. A temporal change after an entry does not prove the substitute caused it or reveal the coach's intention.

## Goalkeeper as part of team structure

Analyze the goalkeeper in possession as a possible extra passing option: starting position, available angles, pressure, first touch, pass range, target and support for the next action. A short goal-kick build-up can create an extra line but can also increase turnover risk near goal; a direct route can bypass pressure but requires a target, runner and second-ball support. UEFA's season reports show that teams use different goal-kick strategies, not that one is always best.

Out of possession, assess starting depth relative to defensive line and ball, ability to cover space behind, position for crosses/cutbacks, and one-on-one decisions. These assessments need aligned goalkeeper, ball, attacker and defender positions and the shot/cross context. One frame may show a position; it does not establish a trend, anticipation skill or decision quality. FIFA's goalkeeper resources cover build-up and transition positioning as distinct contexts ([build-up](https://www.fifatrainingcentre.com/en/practice/elite-sessions/goalkeeper/goalkeepersbuildup.php), [transition positioning](https://www.fifatrainingcentre.com/en/game/individual-qualities/goalkeeping/goalkeeper-positioning-in-transition-from-attack-to-defence.php)).

## Restarts and second phases

Treat corners, free kicks, penalties, throw-ins and goal kicks as distinct phases with their own laws, player assignments and temporal boundaries. For each restart, capture delivery, first movement, blockers/screens where visible, target zone, first contact, clearance, edge occupation, rebound/second ball and transition protection. Separate a designed routine hypothesis from an observed movement pattern; repeated motion alone does not reveal an instruction.

For defending, identify whether the observable organization appears zonal, player-oriented or mixed, then inspect contest, keeper traffic, near/far-post protection, edge cover and counter outlet. Do not mark the assignment of a player unless the evidence shows it. A clearance is not the end of the phase if possession remains contested. FIFA's corner analysis includes examples of freeing an edge shooter, blocker/runner movements and zonal/player-oriented organization; these are examples to compare, not templates ([FIFA set-play analysis](https://www.fifatrainingcentre.com/en/game/game-analysis/set-plays/)).

## Late game, dismissals and unusual states

Condition interpretation on score, time semantics, players on the field, restart type and accumulated evidence. A narrow lead or need to score may coincide with deeper defending, more direct play, altered width, additional box presence or fewer players behind the ball. These are tactical possibilities, not facts about intent. A dismissal changes numerical availability and may prompt role redistribution, but whether the team changes formation or risk posture must be observed. Last-minute fouls require law context plus transition, location, player, restart and card evidence. Avoid unsupported claims about time-wasting, deliberate injury, fatigue or referee intent.

## Player development, practice and format effects

Football performance is not only technical or tactical. The FA's Four Corner Model treats technical/tactical, physical, psychological and social development as connected lenses and cautions coaches to adapt support to the individual ([The FA Four Corner Model](https://www.thefa.com/bootroom/resources/coaching/the-fas-4-corner-model)). This is a player-development framework, not a match-state classifier. Age, maturation, sex, competition level, training history and format matter when transferring findings.

Small-sided/conditioned games change the information and action demands by manipulating player numbers, area per player, pitch geometry, targets, touch constraints, rules and work/rest pattern. Research reviews report that such changes can alter technical actions and collective tactical behavior, but effects are design- and sample-dependent; a practice task does not establish transfer to a full match ([Bezerra et al., systematic review](https://doi.org/10.1177/1747954118769183), [Sarmento et al., systematic review](https://doi.org/10.1080/24748668.2018.1517288)). The model should not recommend a training prescription from match data without age, workload, health and coaching context.

## Match analysis, metrics and causal limits

Football analysis must define actions and outcomes operationally, record sample/population and include match situation. A review of match-analysis research found variation in outcome variables and emphasized standardized definitions and situational/interactional context ([Sarmento et al., match-analysis review](https://pubmed.ncbi.nlm.nih.gov/24787442/)). At minimum, metrics should identify formula, unit, denominator, time/phase window, player/team scope, provider/version, event coverage, missingness, calibration and permitted comparisons.

- Counts depend on exposure; use a justified denominator before comparing teams or players.
- Per-90 values do not control for possession, role, phase, opposition or event coverage.
- Passing completion does not measure progression, difficulty or decision quality by itself.
- Shot volume, goals and saves do not alone establish chance quality or goalkeeper performance.
- Distance and high-speed running do not diagnose fatigue or intent.
- A formation/press label requires an operational definition and validated observation method.
- Before/after comparisons show association. Causal claims need an appropriate design and controls.

AI-based tactical analysis can map collective movement, shape, networks and patterns, but input modality, tracking coverage, sample selection, competition, sex and age constrain generalization. A recent systematic review of AI methods in football tactical/collective-dynamics research describes a diverse method landscape and explicit inclusion boundaries ([AI and collective dynamics review](https://pmc.ncbi.nlm.nih.gov/articles/PMC12163489/)). PitchMind should state when its analysis is event-only versus tracking/video-assisted and must not imply that an event feed observes body orientation, off-ball movement or collective shape.

## Knowledge-base article record

The machine-readable [reviewed article catalogue](fifa-training-centre-article-catalog.v1.json) records topic, code, age/competition context, source type, publication date where known, review date, a short paraphrase and use/rights guidance. Before adding a source, verify the direct URL, publisher, content type, context and date. Prefer the law-making authority for rules, governing-body technical reports for their competitions, original research for empirical claims and transparent systematic reviews for broad evidence. Record limitations and do not treat a later or more popular article as automatically stronger evidence.

