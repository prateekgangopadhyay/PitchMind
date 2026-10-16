# FIFA Training Centre knowledge index

## Purpose and scope

This index connects PitchMind's original tactical knowledge base to the public [FIFA Training Centre](https://www.fifatrainingcentre.com/en/). It covers the site's football-knowledge navigation and selected high-value references for formation, team shape, movement, phases, attacking combinations, defending, transitions, set plays, match analysis, player development, goalkeeping, futsal and beach soccer.

The Training Centre is a large, changing library, not a finite article bundle. This is a curated topic map, not a claim that every current or future FIFA article has been ingested or reviewed. FIFA material is paraphrased and linked; do not copy full articles, drills, images or videos into PitchMind. The references describe general coaching concepts. They are not canonical match evidence and do not establish what happened in a PitchMind fixture.

## Companion playbooks and machine-readable catalog

- [Football tactical playbooks](FOOTBALL-TACTICAL-PLAYBOOKS.md) — matchups, phases, role fit, goalkeeper, set pieces and late-match states.
- [Reviewed FIFA article catalogue](fifa-training-centre-article-catalog.v1.json) — topic, code, age/competition context, source type and direct link per entry.

## Training Centre topic map

| FIFA section | Football knowledge represented | PitchMind use |
| --- | --- | --- |
| [The Game](https://www.fifatrainingcentre.com/en/game/the-game.php) | Game principles, tournament observations, match analysis, tactical trends, individual and team actions, and set-play observations | Vocabulary and analytical lenses; every match claim still needs selected-match evidence |
| [Tournament Observations](https://www.fifatrainingcentre.com/en/game/tournament-observations.php) | Technical observations across competitions, teams, phases and trends | Context for concepts, not a substitute for the synthetic feed or current-match facts |
| [Game Library](https://www.fifatrainingcentre.com/en/resources/game-library/) | Video examples grouped as in possession, out of possession, transition to attack, transition to defend, set plays and position-specific actions | Phase taxonomy and examples for explaining observable mechanisms |
| [Football Language](https://www.fifatrainingcentre.com/en/resources-tools/football-language/index.php) | Operational vocabulary and video examples for on-ball/off-ball actions, players, teams and interactions | Terminology crosswalk; PitchMind source contracts and canonical definitions remain authoritative |
| [Elite practice](https://www.fifatrainingcentre.com/en/practice/elite.php) | Elite-team and academy practice sessions, including build-up, progression, overloads, finishing, defending and restarts | Tactical mechanism catalogue; do not treat a practice pattern as proof a match team used it |
| [Talent development](https://www.fifatrainingcentre.com/en/practice/talent_development.php) | Age-appropriate development, training frameworks, game/player/unit/team scales, build-and-progress and press-and-regain | Scales of analysis and development context; avoid transferring age-group outcomes to elite matches without qualification |
| [Grassroots and youth](https://www.fifatrainingcentre.com/en/practice/grassroots.php) | Learning environment, age-appropriate practice and foundational technique/tactics | Developmental context only; adapt claims to competition and age group |
| [Beach soccer](https://www.fifatrainingcentre.com/en/practice/beach-soccer.php) | Beach-soccer systems, rotations, goalkeeper participation, restarts and small-sided tactical organization | Distinct code-specific knowledge; never apply its formations or rules to 11-a-side football by default |
| [Futsal](https://www.fifatrainingcentre.com/en/practice/futsal.php) | Futsal formations, rotations, pressing, set plays, goalkeeper-as-attacker and transition | Distinct code-specific knowledge; keep its player count, dimensions and laws explicit |
| [Interviews](https://www.fifatrainingcentre.com/en/environment/interviews.php) | Practitioner reflections and experience | Qualitative perspective; attribute claims to the speaker and do not convert opinion into match fact |
| [Coaching development](https://www.fifatrainingcentre.com/en/environment/coaching-development.php) | Coach learning, methodology, reflection and practice design | Knowledge provenance and learning context |
| [Technical leadership](https://www.fifatrainingcentre.com/en/environment/technicalleadership.php) | Federation/club technical structures and long-term football development | Organization and programme context, not an individual match detector |
| [High performance](https://www.fifatrainingcentre.com/en/environment/high-performance.php) | Performance environment, physical preparation, recovery and multidisciplinary practice | Context for performance hypotheses; player workload/fatigue claims need validated data |
| [Goalkeeping](https://www.fifatrainingcentre.com/en/environment/goalkeeping.php) | Goalkeeper technique, decisions, distribution, positioning and integration with team phases | Goalkeeper-specific analysis when the source data includes the relevant actions and locations |

FIFA navigation and article availability may change. Recheck each source before adding a new model-facing claim. Categories such as futsal and beach soccer are intentionally kept distinct from association football's 11-a-side laws and dimensions.

## Formation and phase-dependent shape

A formation label is a shorthand for a nominal arrangement, usually derived from a verified lineup and an explicit counting convention. It is not a complete description of how a team behaves. The Training Centre's [team-shape metric](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--team-shape.php) explicitly considers positional structures in and out of possession; its [Scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php) material connects team, unit and player views.

PitchMind should distinguish:

1. **Starting formation:** the lineup's nominal organization, only when supported by an authoritative lineup/role source and a declared notation convention.
2. **In-possession structure:** how players occupy lanes, lines and depth while building, progressing or attacking. It may be asymmetric and may change by ball location and opponent pressure.
3. **Out-of-possession block:** the team's line arrangement, width, depth, compactness and ball-side shift while defending. A formation label alone does not establish these properties.
4. **Pressing shape:** the relationship between the first pressure, cover/support, screened routes and the rest of the team. One player pressing is not a team press.
5. **Transition and recovery shape:** the team's immediate reaction after a verified regain or loss, including forward outlets, counterpress, delay and recovery runs.
6. **Restart organization:** the distinct organization for corners, free kicks, throw-ins, goal kicks and penalties; do not assume open-play roles persist.

Illustrative transformations such as a nominal back four building with three players behind the ball, or wide players dropping to form a midfield line, are tactical possibilities, not templates to impose on a match. In a documented FIFA practice example, players can start in a 4-4-2-like arrangement while attacking in a 4-2-4-like structure. That example shows why formation and phase-shape labels must remain separate; it says nothing about a PitchMind match ([FIFA: Combination play in the final third](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/combination-play-in-the-final-third.php)).

To describe a structure, analyze the team, then relevant unit, then player interactions. Validate player identity, team, coordinates, coordinate convention, pitch dimensions, attack direction, time alignment, phase, frame cadence and coverage first. Compare repeated observations in like phases. Then describe line relationships, width/depth occupation, staggering, central/half-space access, protection behind the ball and opponent response. Separate source locations from derived line/shape classifications and tactical interpretation. If the feed is event-only or positional semantics are unknown, report that the formation/phase shape cannot be established.

The machine-readable `formation-and-phase-shape` card adds this evidence discipline to all tactical profiles. It is a bounded explanation aid, not a formation detector. The existing deterministic verification remains authoritative for claims about match facts.

### How common formations combine defensive and attacking roles

The advantages below are conditional design possibilities. They depend on player capabilities, roles, the opponent, the match phase and the team's instructions. A formation label alone does not establish that a team uses the described movement or gains the stated advantage. The [FIFA scales-of-the-game framework](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php) is useful here because it relates team organization to unit and player interactions; FIFA's [team-shape metric](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--team-shape.php) distinguishes structures in and out of possession.

| Nominal setup | Defensive-attacking combinations that may create an advantage | Main trade-off to inspect |
| --- | --- | --- |
| **4-3-3** | A pivot connects the centre-backs to two 8s; the 8s can support wide forwards, arrive beyond the ball or cover a full-back. A winger, full-back and 8 can rotate between holding width, overlap and underlap, producing a wide 2v1 or a third-player route. The front three can stretch the last line while the midfield supports counter-pressure. | An advancing full-back can leave a channel behind; a lone pivot may face a central overload; a front three can become disconnected if the midfield cannot progress the ball.
| **4-2-3-1** | Two pivots can divide screening and progression duties; the 10 links them to the striker and can exploit space between opponent lines. Wide forwards can pin or narrow while full-backs provide outside width. The pair of pivots can cover an advancing full-back and support second balls. | If the 10 is screened or the striker isolated, the attack may lack a central link. If both pivots advance together, central protection can disappear; if both stay deep, the front four may be detached.
| **4-4-2** | Two strikers can coordinate pressure on centre-backs, with one pressing and the other screening/supporting, then become paired outlets on a regain. Two midfield banks can protect central routes and shift together. A wide midfielder and full-back can share the flank: one engages while the other covers or overlaps. | A pair of central midfielders can be outnumbered by a three-player midfield. Wide midfielders may be pulled deep, leaving the forwards isolated; full-back overlaps require cover behind them.
| **3-5-2** | Three centre-backs can support circulation and cover behind advancing wing-backs. Wing-backs supply width and can combine with a shuttling midfielder; a central screen protects the middle. Two forwards can occupy both centre-backs, combine, or split one checking short and one threatening depth. | Space behind a wing-back is a counter route. The wide centre-back may be drawn outward, opening a central channel; the far wing-back must recover or defend the back post.
| **3-4-3** | A front three can press the opponent's back line and threaten width and inside channels. Wing-backs can advance outside the front line while wide forwards move inside; three centre-backs can cover the first transition behind them. | The two central midfielders may be overloaded. Wing-backs need substantial support and recovery coverage, while wide centre-backs may be pulled away from central protection.
| **4-2-2-2** | Two narrow attacking midfielders can combine centrally with a pair of forwards, creating short passing links and runs around the ball. The full-backs can provide the width that the narrow attacking line leaves open. | The structure can surrender natural wide occupation and depend on full-back timing. If both full-backs advance, the pivots and centre-backs must coordinate cover against wide transitions.
| **4-1-4-1** | A holding midfielder screens the centre-backs, while a four-player midfield line can close central access or support the press. The lone forward can screen a pivot and lead pressure; nearby midfielders can join a regain or support the striker. | The lone forward may lack support during build-up or counter-attacks. A large gap between that forward and midfield line can make pressure easy to bypass.

#### Role combinations that transfer across formations

- **Centre-back + pivot:** stagger their positions so the pivot offers a forward passing lane while remaining able to protect the centre if possession is lost. A flat or hidden pivot may force circulation around the opponent; a pivot who advances without cover can expose the central lane.
- **Full-back + winger + 8:** agree who holds width, attacks outside, underlaps inside and supports behind the ball. The point is to create different heights and angles, not to send all three into one channel.
- **Pivot pair + 10:** one pivot may hold while the other supports progression; the 10 offers a line-breaking target. The pair can exchange duties, but both advancing or both remaining detached can weaken balance.
- **Striker pair/front three + midfield runners:** one forward may pin or threaten depth while another checks short; an 8 or 10 can use the space created. Verify the run and defender response before calling it a coordinated rotation.
- **Wing-back + wide centre-back + far-side cover:** when a wing-back advances, the wide centre-back can protect the channel and the remaining defenders/pivot can secure the centre. The far-side wing-back may need to narrow or recover; confirm the observed assignments before describing a back-three rest defence.
- **Pressing forward + midfield line + back line:** the first presser needs cover, screened routes and a coordinated line behind. Without those relationships, pressure can open a pass through the middle or space behind the defence.

For every claimed advantage, describe the trigger, actual player/role relationships, space created or protected, opponent response, resulting action and uncertainty. Compare like phases and report the matching risk alongside the benefit. The machine-readable `formation-player-combinations` card supplies a concise version to relevant tactical specialists; this knowledge is not a formation detector.

## Curated article and concept references

### Formation, shape and spatial analysis

- [FIFA: Team shape metric](https://www.fifatrainingcentre.com/en/fwc2022/efi-metrics/efi-metric--team-shape.php) — positional structures in and out of possession.
- [FIFA: Scales of the game](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/training-framework/scales-of-the-game.php) — team, unit and player scales; dynamic roles and numerical relationships.
- [FIFA: Team organisation out of possession](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php) — horizontal/vertical compactness, shifting and pressure.
- [FIFA: Football Language](https://www.fifatrainingcentre.com/en/resources-tools/football-language/index.php) — operational terminology and on/off-ball examples.

### In-possession play and overloads

- [FIFA: Building from the back](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/building_from_the_back_skinner.php) — build-up organization, width and playing through/around pressure.
- [FIFA: Creating and exploiting overloads](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/creating-and-exploiting-overloads.php) — creating local numerical and positional advantages.
- [FIFA: Developing wide attacking patterns](https://www.fifatrainingcentre.com/en/practice/elite-sessions/clubs-and-academies/english-academies/newcastle-u-15-developing-wide-attacking-patterns.php) — overlap, underlap and wide 2v1 examples.
- [FIFA: Combination play in the final third](https://www.fifatrainingcentre.com/en/practice/elite-sessions/in-possession/combination-play-in-the-final-third.php) — rotations, third-player movements and final-third combinations.
- [FIFA: 8v8 building through middle areas](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/build-and-progress/8v8-team-game-building-up-through-middle-areas.php) — central progression against a high press.

### Defending and pressing

- [FIFA: Pressing and defensive shape](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/press-and-regain/4v4-plus-4-small-sided-game-pressing-and-defensive-shape.php) — pressure, cover and central route denial.
- [FIFA: Compact defending](https://www.fifatrainingcentre.com/en/practice/talent-coach-programme/press-and-regain/8v7-team-game-compact-defending.php) — compact shape and high-press scenarios.
- [FIFA: Out-of-possession team organization](https://www.fifatrainingcentre.com/en/game/game-analysis/out-of-possession/team-organisation--out-of-possession-.php) — shifting, block structure and pressure.

### Rules and event context

For substitutions, match duration and time lost, or fouls and misconduct, use the current [IFAB Laws of the Game](https://www.theifab.com/laws/latest/). FIFA coaching articles may explain tactical context; IFAB remains the authority for the laws, and PitchMind should not adjudicate referee correctness without an appropriate authoritative decision source.

## Source-to-knowledge workflow

When curating another FIFA article, add its direct canonical URL, title, topic(s), football code, age/competition scope, source type (analysis, observation, interview, practice/session, metric or glossary), access/review date, rights status and a short original paraphrase. Record what evidence would be needed to apply the concept to a match. Preserve a distinction between source example, general principle, derived detector output and canonical PitchMind match evidence. Recheck source availability and attribution before publishing. Do not scrape or bulk-copy the Training Centre library.

## Use by the model

A source-linked concept can explain what evidence might mean; it cannot supply absent facts. For each answer, cite the selected evidence packet for match statements, identify interpretation as interpretation, state missing context, and abstain from formation or shape labels where required lineup/positional evidence is absent. Never infer personal endorsement, private knowledge, or a coach's intent from the list of named managers in the product request.
