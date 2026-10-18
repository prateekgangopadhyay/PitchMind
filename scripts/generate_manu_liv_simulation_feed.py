import json
import os

with open('data/simulations/manu-liv-match-dataset.json') as f:
    events = json.load(f)

# Find goals
goals = [e for e in events if e.get('eventType') == 'Goal']
# Goal 1: 854s Bruno Fernandes (MANU)
# Goal 2: 1653s Mohamed Salah (LIV)
# Goal 3: 2464s Luis Díaz (LIV)
# Goal 4: 3505s Marcus Rashford (MANU)
# Goal 5: 4482s Darwin Núñez (LIV)
# Goal 6: 5335s Kobbie Mainoo (MANU)

# Selected snapshot event: Goal 6 or late second half
selected_event = [e for e in events if e['sequence'] == 2100][0]
players_sample = selected_event.get('playerPositions', [])

# Map players for intelligentPitch
pitch_players = []
for p in players_sample:
    is_home = (p['teamName'] == 'Manchester United')
    pitch_players.append({
        'id': f"p_{p['jerseyNumber']}",
        'x': p['x'],
        'y': p['y'],
        'team': 'home' if is_home else 'away',
        'number': p['jerseyNumber'],
        'name': p['playerName'],
        'position': p['position'],
        'speed': p.get('speed', 0),
        'distanceCovered': p.get('distanceCovered', 0),
        'heatIndex': p.get('heatIndex', 0.5),
        'passAccuracy': p.get('passAccuracy', 80),
        'touches': p.get('touches', 25),
        'stamina': p.get('stamina', 85),
        'heartRate': p.get('heartRate', 150)
    })

# Key timeline events (extract 25 top events across the match)
key_indices = [
    1, 45, 120, 205, 341, 410, 520, 680, 810, 940, 
    1080, 1200, 1340, 1480, 1620, 1750, 1890, 2020, 2120, 2190
]
# Ensure all 6 goals are in the timeline
goal_seqs = {g['sequence'] for g in goals}
timeline_events = []
for e in events:
    if e['sequence'] in goal_seqs or e['sequence'] in key_indices or e['eventType'] in ['KickOff', 'RedCard', 'HalfTime', 'FullTime']:
        clock_min = f"{e['clockSeconds'] // 60}'"
        is_goal = e['eventType'] == 'Goal'
        is_key = is_goal or e['eventType'] in ['KickOff', 'RedCard', 'Shot', 'LineBreakingPass']
        
        detail = f"{e['playerName']} {e['eventType']}"
        if is_goal:
            detail = f"GOAL! {e['playerName']} scores with precision into the bottom corner!"
        elif e['eventType'] == 'Pass':
            detail = f"{e['playerName']} plays progressive pass to {e.get('receiverName') or 'midfield'}"
        elif e['eventType'] == 'Tackle':
            detail = f"{e['playerName']} wins crucial tackle in middle third"
        elif e['eventType'] == 'PressureApplied':
            detail = f"{e['playerName']} applies high intensity pressing in Zone 14"
        elif e['eventType'] == 'Shot':
            detail = f"{e['playerName']} unleashes powerful shot on target"
            
        icon = '⚽' if is_goal else '★' if is_key else '➔'
        timeline_events.append({
            'id': f"ev-{e['sequence']}",
            'sequence': e['sequence'],
            'clock': clock_min,
            'clockSeconds': e['clockSeconds'],
            'period': e['period'],
            'type': e['eventType'],
            'team': e.get('teamName', 'Match'),
            'playerName': e.get('playerName'),
            'detail': detail,
            'icon': icon,
            'isKey': is_key,
            'highlight': is_goal,
            'x': e.get('pitchX', 50),
            'y': e.get('pitchY', 50)
        })

timeline_events.sort(key=lambda x: x['sequence'])

# Teams squad list
manu_players = [p for p in players_sample if p['teamName'] == 'Manchester United']
liv_players = [p for p in players_sample if p['teamName'] == 'Liverpool']

simulation_data = {
    'authPreview': {
        'headline': 'Understand the Premier League beyond the scoreline',
        'description': 'Evidence-grounded tactical intelligence, live sequence tracking, and verified spatial insights for elite analysts and passionate supporters.',
        'featuredMatch': {
            'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
            'homeTeam': 'Manchester United',
            'homeLogo': '/crests/manchester-united.svg',
            'awayTeam': 'Liverpool',
            'awayLogo': '/crests/liverpool.svg',
            'homeScore': 3,
            'awayScore': 3,
            'clock': '88:55',
            'status': 'Live Match',
            'tacticalSnippet': 'Manchester United and Liverpool end-to-end clash: high press turnovers in Zone 14 leading directly to decisive transitions (8 regains in attacking third)',
            'pitchSnippet': {
                'pressureZone': 'Attacking Third / Zone 14',
                'regainsCount': 8,
                'players': [
                    {'id': 'p1', 'x': 68, 'y': 52, 'team': 'home', 'number': 8, 'name': 'B. Fernandes'},
                    {'id': 'p2', 'x': 72, 'y': 38, 'team': 'home', 'number': 10, 'name': 'Rashford'},
                    {'id': 'p3', 'x': 54, 'y': 48, 'team': 'home', 'number': 37, 'name': 'Mainoo'},
                    {'id': 'p4', 'x': 50, 'y': 55, 'team': 'home', 'number': 18, 'name': 'Casemiro'},
                    {'id': 'p5', 'x': 45, 'y': 65, 'team': 'away', 'number': 11, 'name': 'Salah'},
                    {'id': 'p6', 'x': 38, 'y': 48, 'team': 'away', 'number': 4, 'name': 'Van Dijk'}
                ]
            }
        },
        'valueBadges': [
            {'title': 'Zero Speculative Stats', 'detail': 'Every metric derived strictly from verified candidate telemetry frames.'},
            {'title': 'Sub-Second SignalR Fan-Out', 'detail': 'Deterministic outbox streaming over Kafka, PostgreSQL and Redis.'},
            {'title': 'Azure AI Foundry Grounded', 'detail': 'Evaluated with Azure OpenAI models strictly against observed pitch bounds.'},
            {'title': 'Verified Telemetry Grounding', 'detail': 'Every metric derived strictly from verified candidate telemetry frames.'}
        ],
        'trustCertifications': [
            {'title': 'Enterprise Architectural Integrity', 'detail': 'Clean Hexagonal architecture with decoupled projection readers and strict outbox leasing.'},
            {'title': 'Synthetic Provenance Confirmed', 'detail': 'Owner-certified candidate match dataset with 2,199 ordered events and full tracking telemetry.'}
        ]
    },
    'hub': {
        'eyebrow': 'Home · match library',
        'title': 'Choose a match',
        'subtitle': 'Explore recorded and live matches. Scores, tracking, and tactical phases stream directly from the match engine.',
        'heroMatch': {
            'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
            'competition': 'Premier League · Super Sunday',
            'stadium': 'Old Trafford',
            'homeTeam': 'Manchester United',
            'homeLogo': '/crests/manchester-united.svg',
            'homeFormation': '4 - 2 - 3 - 1',
            'homeScore': 3,
            'awayScore': 3,
            'awayTeam': 'Liverpool',
            'awayLogo': '/crests/liverpool.svg',
            'awayFormation': '4 - 3 - 3',
            'clock': '88:55',
            'period': 'Second Half',
            'leadingInsight': 'Manchester United 3 - 3 Liverpool: Bruno Fernandes orchestrating transitions, Kobbie Mainoo scoring late equalizer, while Salah and Díaz threaten wide channels.'
        },
        'otherLiveMatches': [
            {
                'matchId': 'sim-match-ars-che',
                'homeTeam': 'Arsenal',
                'homeLogo': '/crests/arsenal.svg',
                'homeScore': 2,
                'awayScore': 1,
                'awayTeam': 'Chelsea',
                'awayLogo': '/crests/chelsea.svg',
                'clock': '74:10',
                'status': 'Second Half'
            },
            {
                'matchId': 'sim-match-mci-tot',
                'homeTeam': 'Manchester City',
                'homeLogo': '/crests/manchester-city.svg',
                'homeScore': 1,
                'awayScore': 1,
                'awayTeam': 'Tottenham',
                'awayLogo': '/crests/tottenham.svg',
                'clock': '61:45',
                'status': 'Second Half'
            },
            {
                'matchId': 'sim-match-avl-new',
                'homeTeam': 'Aston Villa',
                'homeLogo': '/crests/aston-villa.svg',
                'homeScore': 0,
                'awayScore': 0,
                'awayTeam': 'Newcastle United',
                'awayLogo': '/crests/newcastle-united.svg',
                'clock': '52:30',
                'status': 'Second Half'
            }
        ],
        'whatMattersNow': [
            {
                'rank': 1,
                'title': 'Manchester United: High Turnover Recovery in Zone 14 leading directly to decisive goal opportunity.',
                'matchTag': 'MANU vs LIV'
            },
            {
                'rank': 2,
                'title': 'Liverpool: Right-Flank Overload via Trent Alexander-Arnold & Mohamed Salah creating 4 box entries.',
                'matchTag': 'MANU vs LIV'
            },
            {
                'rank': 3,
                'title': 'Transition Dynamics: Kobbie Mainoo progressive carries advancing into attacking third (88% pass accuracy).',
                'matchTag': 'MANU vs LIV'
            },
            {
                'rank': 4,
                'title': 'High Press Intensity: Manchester United front three forcing 12 regains in Liverpool defensive half.',
                'matchTag': 'MANU vs LIV'
            }
        ],
        'keyMoments': [
            {
                'id': 'km-1',
                'clock': "14'",
                'title': 'Bruno Fernandes Goal after Zone 14 Turnover',
                'description': 'Rapid transition initiated by Casemiro tackle, Fernandes finishes into bottom left corner.'
            },
            {
                'id': 'km-2',
                'clock': "27'",
                'title': 'Mohamed Salah Equalizer from Half-Space Overload',
                'description': 'Alexander-Arnold diagonal pass finds Salah cutting inside past defensive line.'
            },
            {
                'id': 'km-3',
                'clock': "89'",
                'title': 'Kobbie Mainoo Stunning Decisive Goal',
                'description': 'Mainoo curler from edge of box following sustained sustained pressing sequence.'
            }
        ],
        'leagueSnapshot': [
            {'rank': 1, 'team': 'Liverpool', 'logo': '/crests/liverpool.svg', 'p': 28, 'w': 20, 'd': 5, 'l': 3, 'gd': '+38', 'pts': 65},
            {'rank': 2, 'team': 'Manchester City', 'logo': '/crests/manchester-city.svg', 'p': 28, 'w': 19, 'd': 6, 'l': 3, 'gd': '+35', 'pts': 63},
            {'rank': 3, 'team': 'Arsenal', 'logo': '/crests/arsenal.svg', 'p': 28, 'w': 18, 'd': 7, 'l': 3, 'gd': '+33', 'pts': 61},
            {'rank': 4, 'team': 'Manchester United', 'logo': '/crests/manchester-united.svg', 'p': 28, 'w': 16, 'd': 6, 'l': 6, 'gd': '+18', 'pts': 54},
            {'rank': 5, 'team': 'Chelsea', 'logo': '/crests/chelsea.svg', 'p': 28, 'w': 14, 'd': 7, 'l': 7, 'gd': '+12', 'pts': 49},
            {'rank': 6, 'team': 'Tottenham', 'logo': '/crests/tottenham.svg', 'p': 28, 'w': 13, 'd': 6, 'l': 9, 'gd': '+8', 'pts': 45}
        ],
        'upcomingFixtures': [],
        'suggestedQueries': [
            'Why is Manchester United pressing so high against Liverpool?',
            'How is Trent Alexander-Arnold overloading the right half-space?',
            'Analyze Kobbie Mainoo defensive transition efficiency'
        ]
    },
    'match': {
        'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
        'homeTeam': 'Manchester United',
        'homeFormation': '4 - 2 - 3 - 1',
        'awayTeam': 'Liverpool',
        'awayFormation': '4 - 3 - 3',
        'homeScore': 3,
        'awayScore': 3,
        'clock': '88:55',
        'period': 'Second Half',
        'stadium': 'Old Trafford',
        'competition': 'Premier League',
        'homeLogo': '/crests/manchester-united.svg',
        'awayLogo': '/crests/liverpool.svg',
        'leadingInsight': "Manchester United's high pressing trap in Zone 14 disrupting Liverpool's build-up and generating quick transitions.",
        'intelligentPitch': {
            'clock': '88:55',
            'pressureZone': 'Zone 14 / Attacking Third',
            'regainsCount': 12,
            'ballX': selected_event.get('ballX', 52.4),
            'ballY': selected_event.get('ballY', 48.2),
            'players': pitch_players
        },
        'timeline': timeline_events,
        'whyItMatters': {
            'title': 'High Turnover Recovery in Zone 14 Exploding into Decision',
            'classification': 'Supported interpretation',
            'summary': 'Manchester United deployed an aggressive mid-to-high block, baiting Liverpool center-backs into central passes before Mainoo and Casemiro clamped down on the receiver, executing 8 high regains.',
            'confidence': 0.94,
            'mechanismSteps': [
                {'step': 1, 'text': 'Liverpool center-back Virgil van Dijk steps forward attempting line-breaking pass.'},
                {'step': 2, 'text': 'Casemiro steps up to intercept pass into the half-space.'},
                {'step': 3, 'text': 'Kobbie Mainoo secures loose ball and plays immediate progressive release to Bruno Fernandes.'},
                {'step': 4, 'text': 'Fernandes slides pass into channel for Rashford on the break.'}
            ]
        },
        'evidence': {
            'eventCount': 2199,
            'trackingFramesCount': 2199,
            'conditions': [
                'Zone 14 pressure index exceeded 0.85 threshold',
                'Turnover occurred within 3.2 seconds of defensive screen compression',
                'Pass completion through central corridor limited to 68%'
            ],
            'limitations': [
                'Grounded strictly on candidate telemetry coordinates (0-100 normalized scale)',
                'Calculations verified against canonical pitch coordinate bounds'
            ]
        },
        'aiAnalyst': {
            'text': "Tactical evaluation via Azure AI Foundry: Manchester United 3 - 3 Liverpool has exhibited exceptional pressing intensity. United's 4-2-3-1 structure effectively squeezed Liverpool's central progression, forcing Trent Alexander-Arnold to invert into congested midfield areas. The key decisive battle occurred in Zone 14, where Kobbie Mainoo and Casemiro produced 12 ball recoveries.",
            'source': 'Microsoft Azure AI Foundry (gpt-4o-mini-tactical)',
            'clockSeconds': 5335,
            'insightId': 'manu-liv-tactical-press',
            'audience': 'analyst',
            'limitations': [
                'Grounded strictly on verified match telemetry evidence',
                'Evaluated against canonical pitch coordinate bounds'
            ]
        },
        'keyMoment': {
            'clock': "88:55",
            'title': 'Kobbie Mainoo Equalizing Wonder Goal',
            'description': 'Mainoo curls into the far corner following intense counter-pressing sequence on edge of penalty box.',
            'classifications': ['ObservedFact', 'GoalExploitation']
        },
        'sequenceReplay': [
            {'step': 1, 'title': 'High pressure applied by Fernandes', 'active': False},
            {'step': 2, 'title': 'Casemiro intercepting clearance', 'active': False},
            {'step': 3, 'title': 'Mainoo touch and curling finish', 'active': True}
        ]
    },
    'teams': [
        {
            'id': 'man-united',
            'name': 'Manchester United',
            'shortName': 'Man United',
            'formation': '4 - 2 - 3 - 1',
            'leagueRank': 4,
            'points': 54,
            'form': ['W', 'D', 'W', 'W', 'D'],
            'manager': 'Erik ten Hag',
            'avgPossession': 53.4,
            'xGPer90': 1.84,
            'xGAPer90': 1.22,
            'ppda': 9.8,
            'summary': 'Dynamic transition side utilizing aggressive midfield pressing traps in Zone 14 with rapid vertical progression via Fernandes, Rashford, and Garnacho.',
            'logo': '/crests/manchester-united.svg',
            'zonesOfControl': {
                'attackingThird': '38%',
                'middleThird': '47%',
                'defensiveThird': '15%'
            },
            'progressionRoutes': {
                'leftFlank': '36%',
                'central': '38%',
                'rightFlank': '26%'
            },
            'keyMetrics': [
                {'label': 'PPDA', 'value': '9.8', 'benchmark': 'Elite Pressing'},
                {'label': 'Turnover Regains', 'value': '12', 'benchmark': 'Match High'},
                {'label': 'Direct Speed', 'value': '2.14 m/s', 'benchmark': 'Top 5% EPL'},
                {'label': 'Box Entries', 'value': '34', 'benchmark': 'Above Avg'}
            ],
            'pressingStats': {
                'highPressRegains': 12,
                'counterPressingEff': '68%',
                'middleThirdTackles': 24,
                'defensiveLineHeight': '52.4m'
            },
            'keyPlayers': [
                {'name': 'Bruno Fernandes', 'role': 'Attacking Midfield', 'number': 8, 'rating': '8.8'},
                {'name': 'Kobbie Mainoo', 'role': 'Central Midfield', 'number': 37, 'rating': '8.6'},
                {'name': 'Marcus Rashford', 'role': 'Left Wing', 'number': 10, 'rating': '8.2'},
                {'name': 'André Onana', 'role': 'Goalkeeper', 'number': 24, 'rating': '7.9'}
            ],
            'recentMatches': [
                {'opponent': 'Liverpool', 'score': '3 - 3', 'result': 'D'},
                {'opponent': 'Everton', 'score': '2 - 0', 'result': 'W'},
                {'opponent': 'Fulham', 'score': '2 - 1', 'result': 'W'},
                {'opponent': 'Brentford', 'score': '1 - 1', 'result': 'D'}
            ],
            'tacticalInsights': [
                'Zone 14 compression generates highest xG transition moments in the squad.',
                'Mainoo acting as progressive press-breaker through half-turn distributions.'
            ]
        },
        {
            'id': 'liverpool',
            'name': 'Liverpool',
            'shortName': 'Liverpool',
            'formation': '4 - 3 - 3',
            'leagueRank': 1,
            'points': 65,
            'form': ['W', 'W', 'W', 'W', 'D'],
            'manager': 'Arne Slot',
            'avgPossession': 61.2,
            'xGPer90': 2.15,
            'xGAPer90': 0.98,
            'ppda': 8.6,
            'summary': 'High-possession control with dynamic width and half-space overloads driven by Trent Alexander-Arnold and Mohamed Salah.',
            'logo': '/crests/liverpool.svg',
            'zonesOfControl': {
                'attackingThird': '45%',
                'middleThird': '42%',
                'defensiveThird': '13%'
            },
            'progressionRoutes': {
                'leftFlank': '32%',
                'central': '28%',
                'rightFlank': '40%'
            },
            'keyMetrics': [
                {'label': 'PPDA', 'value': '8.6', 'benchmark': 'Intense Press'},
                {'label': 'Deep Completions', 'value': '14', 'benchmark': 'Match High'},
                {'label': 'Field Tilt', 'value': '58.4%', 'benchmark': 'Dominant'},
                {'label': 'Half-Space Crosses', 'value': '11', 'benchmark': 'Elite'}
            ],
            'pressingStats': {
                'highPressRegains': 15,
                'counterPressingEff': '74%',
                'middleThirdTackles': 19,
                'defensiveLineHeight': '58.2m'
            },
            'keyPlayers': [
                {'name': 'Mohamed Salah', 'role': 'Right Wing', 'number': 11, 'rating': '8.9'},
                {'name': 'Virgil van Dijk', 'role': 'Center Back', 'number': 4, 'rating': '8.5'},
                {'name': 'Trent Alexander-Arnold', 'role': 'Right Back', 'number': 66, 'rating': '8.4'},
                {'name': 'Luis Díaz', 'role': 'Left Wing', 'number': 7, 'rating': '8.3'}
            ],
            'recentMatches': [
                {'opponent': 'Manchester United', 'score': '3 - 3', 'result': 'D'},
                {'opponent': 'Chelsea', 'score': '2 - 1', 'result': 'W'},
                {'opponent': 'Wolves', 'score': '3 - 0', 'result': 'W'},
                {'opponent': 'Bournemouth', 'score': '3 - 1', 'result': 'W'}
            ],
            'tacticalInsights': [
                'Inverted full-back movements by Alexander-Arnold overload central midfield corridors.',
                'Mohamed Salah isolation in wide right 1v1 positions generates highest xA.'
            ]
        },
        {
            'id': 'arsenal',
            'name': 'Arsenal',
            'shortName': 'Arsenal',
            'formation': '4-3-3',
            'leagueRank': 3,
            'points': 61,
            'form': ['W', 'W', 'D', 'W', 'L'],
            'manager': 'Mikel Arteta',
            'avgPossession': 61.4,
            'xGPer90': 2.12,
            'xGAPer90': 0.84,
            'ppda': 8.4,
            'summary': 'High-pressing territorial dominance with wide overloads.',
            'logo': '/crests/arsenal.svg',
            'progressionRoutes': {
                'left': 24,
                'center': 18,
                'right': 58,
                'description': 'Right side focus'
            },
            'keyMetrics': [
                {'label': 'PPDA', 'value': '8.4', 'benchmark': 'Elite Pressing'}
            ],
            'keyPlayers': [
                {'name': 'Bukayo Saka', 'role': 'Right Winger', 'number': 7, 'rating': '8.7'}
            ],
            'recentMatches': [
                {'opponent': 'Manchester City', 'score': '2 - 1', 'result': 'W'}
            ],
            'tacticalInsights': [
                'Right-side overload creating inside space.'
            ]
        }
    ],
    'players': [
        {
            'id': 'saka',
            'name': 'Bukayo Saka',
            'number': 7,
            'role': 'Right Winger',
            'club': 'Arsenal',
            'nationality': 'England',
            'age': 23,
            'minutesPlayed': 67,
            'teamLogo': '/crests/arsenal.svg',
            'matchStats': {
                'goals': 0,
                'assists': 1,
                'shots': 2,
                'keyPasses': 2,
                'passAccuracy': '88%',
                'touches': 48,
                'distanceCovered': '8.4 km',
                'topSpeed': '33.2 km/h'
            },
            'touches': 48,
            'progressiveCarries': 3,
            'chanceCreation': 2,
            'whyItMatters': [
                'Isolating full-back in 1v1 situations',
                'High regains in attacking third',
                'Progression down right channel',
                'Chance creation leader'
            ],
            'analystAssessment': 'Exceptional performance in tactical execution.'
        },
        {
            'id': 'bruno-fernandes',
            'name': 'Bruno Fernandes',
            'number': 8,
            'role': 'Attacking Midfield',
            'club': 'Manchester United',
            'nationality': 'Portugal',
            'age': 30,
            'minutesPlayed': 90,
            'teamLogo': '/crests/manchester-united.svg',
            'matchStats': {
                'goals': 1,
                'assists': 1,
                'shots': 4,
                'keyPasses': 5,
                'passAccuracy': '84%',
                'touches': 88,
                'distanceCovered': '11.8 km',
                'topSpeed': '32.4 km/h'
            },
            'touches': 88,
            'progressiveCarries': 7,
            'chanceCreation': 5,
            'whyItMatters': 'Orchestrated Manchester United pressing triggers in Zone 14 and scored opening goal at 14 minutes.',
            'analystAssessment': 'Bruno Fernandes operated with supreme spatial efficiency against Liverpool, dropping into the left half-space during defensive transitions and accelerating play with first-time passes.'
        },
        {
            'id': 'mohamed-salah',
            'name': 'Mohamed Salah',
            'number': 11,
            'role': 'Right Wing',
            'club': 'Liverpool',
            'nationality': 'Egypt',
            'age': 32,
            'minutesPlayed': 90,
            'teamLogo': '/crests/liverpool.svg',
            'matchStats': {
                'goals': 1,
                'assists': 1,
                'shots': 5,
                'keyPasses': 4,
                'passAccuracy': '82%',
                'touches': 74,
                'distanceCovered': '10.4 km',
                'topSpeed': '33.8 km/h'
            },
            'touches': 74,
            'progressiveCarries': 9,
            'chanceCreation': 4,
            'whyItMatters': 'Scored Liverpool equalizer at 27 minutes and repeatedly pinned Manchester United defensive line.',
            'analystAssessment': 'Salah exploited the space behind Dalot whenever United pushed their defensive line high, creating 3 high-probability cutback scenarios.'
        },
        {
            'id': 'kobbie-mainoo',
            'name': 'Kobbie Mainoo',
            'number': 37,
            'role': 'Central Midfield',
            'club': 'Manchester United',
            'nationality': 'England',
            'age': 19,
            'minutesPlayed': 90,
            'teamLogo': '/crests/manchester-united.svg',
            'matchStats': {
                'goals': 1,
                'assists': 0,
                'shots': 2,
                'keyPasses': 3,
                'passAccuracy': '89%',
                'touches': 72,
                'distanceCovered': '11.2 km',
                'topSpeed': '31.2 km/h'
            },
            'touches': 72,
            'progressiveCarries': 6,
            'chanceCreation': 3,
            'whyItMatters': 'Scored stunning 89th-minute equalizer and won 8 ground duels in central midfield screen.',
            'analystAssessment': 'Exceptional composure in tight central areas, resisting Liverpool counter-press and converting crucial goal from 20 yards.'
        },
        {
            'id': 'virgil-van-dijk',
            'name': 'Virgil van Dijk',
            'number': 4,
            'role': 'Center Back',
            'club': 'Liverpool',
            'nationality': 'Netherlands',
            'age': 33,
            'minutesPlayed': 90,
            'teamLogo': '/crests/liverpool.svg',
            'matchStats': {
                'goals': 0,
                'assists': 0,
                'shots': 1,
                'keyPasses': 1,
                'passAccuracy': '94%',
                'touches': 96,
                'distanceCovered': '9.8 km',
                'topSpeed': '32.1 km/h'
            },
            'touches': 96,
            'progressiveCarries': 4,
            'chanceCreation': 1,
            'whyItMatters': 'Dominant in aerial duels (8 won out of 9) and completed 12 long diagonal balls to wings.',
            'analystAssessment': 'Anchored Liverpool high line against Højlund and Rashford, sustaining team possession in United half.'
        }
    ],
    'insights': [
        {
            'id': 'arsenal-press-city',
            'matchId': 'sim-match-ars-mci',
            'truthClass': 'Evidence-Supported Interpretation',
            'confidence': 0.94,
            'clock': "67'",
            'selectedTimeSeconds': 4044,
            'title': "Arsenal's press is reducing City's central progression",
            'subtitle': 'Mid-to-high pressing structure.',
            'causalChain': [
                {'step': '01', 'title': 'Trigger', 'desc': 'Rice steps up.'},
                {'step': '02', 'title': 'Trap', 'desc': 'Pass blocked.'},
                {'step': '03', 'title': 'Regain', 'desc': 'Turnover won.'},
                {'step': '04', 'title': 'Finish', 'desc': 'Shot executed.'}
            ]
        },
        {
            'id': 'manu-liv-z14-press',
            'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
            'truthClass': 'ObservedFact',
            'confidence': 0.96,
            'clock': "14'",
            'selectedTimeSeconds': 854,
            'title': 'High Turnover Recovery in Zone 14 Exploiting Opening Goal with High Press',
            'subtitle': 'Aggressive central press forced turnover, directly converted by Bruno Fernandes.',
            'pitchDiagram': {
                'pressureZone': 'Zone 14',
                'regainsCount': 8
            },
            'evidenceAtAGlance': [
                {'label': 'Sequence ID', 'value': 'seq-341'},
                {'label': 'Turnover Speed', 'value': '2.8s to shot'},
                {'label': 'Zone', 'value': 'Zone 14 (Central Edge of Box)'},
                {'label': 'xG Value', 'value': '0.44'}
            ],
            'causalChain': [
                {'step': '01', 'title': 'Liverpool Build-Up', 'desc': 'Van Dijk passes into central midfield.'},
                {'step': '02', 'title': 'Pressure Trap', 'desc': 'Casemiro and Mainoo squeeze receiver.'},
                {'step': '03', 'title': 'Ball Recovery', 'desc': 'Turnover won 24m from goal.'},
                {'step': '04', 'title': 'Clinical Finish', 'desc': 'Bruno Fernandes strikes into bottom corner.'}
            ]
        },
        {
            'id': 'liv-manu-right-flank-overload',
            'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
            'truthClass': 'DeterministicDerivation',
            'confidence': 0.92,
            'clock': "27'",
            'selectedTimeSeconds': 1653,
            'title': 'Channel 2 (Half-Space) Overload via Alexander-Arnold and Salah',
            'subtitle': 'Right-sided combination pinned United full-back, yielding Mohamed Salah equalizer.',
            'pitchDiagram': {
                'pressureZone': 'Right Half-Space',
                'regainsCount': 5
            },
            'evidenceAtAGlance': [
                {'label': 'Sequence ID', 'value': 'seq-680'},
                {'label': 'Passes in Build-Up', 'value': '8 consecutive'},
                {'label': 'Channel', 'value': 'Right Half-Space (Channel 4)'},
                {'label': 'xG Value', 'value': '0.52'}
            ],
            'causalChain': [
                {'step': '01', 'title': 'Inverted Full-Back', 'desc': 'Trent Alexander-Arnold tucks into right half-space.'},
                {'step': '02', 'title': 'Overlap Trigger', 'desc': 'Salah draws defender inside.'},
                {'step': '03', 'title': 'Penetrative Pass', 'desc': 'Wall pass slices United left-back.'},
                {'step': '04', 'title': 'Goal Scored', 'desc': 'Salah finishes with curled left-foot strike.'}
            ]
        }
    ],
    'analyst': {
        'matchId': 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
        'selectedTimeSeconds': 5335,
        'matchContextPill': 'Manchester United 3 - 3 Liverpool · 88:55',
        'suggestedPrompts': [
            "Why did Manchester United's high press in Zone 14 succeed in creating goals?",
            "How did Liverpool's right-flank overload with Salah and Alexander-Arnold threaten United?",
            "Evaluate Kobbie Mainoo and Casemiro's defensive midfield screen efficiency",
            "What tactical changes in the second half caused the 3-3 thriller scoreline?"
        ],
        'conversation': [
            {
                'sender': 'user',
                'text': "Explain the tactical mechanism behind Manchester United's Zone 14 pressing regains against Liverpool."
            },
            {
                'sender': 'assistant',
                'text': "Tactical Evaluation via Azure AI Foundry (gpt-4o-mini-tactical):\n\nBased on verified candidate match telemetry (2,199 events, 22 continuous player vectors):\n\n1. Central Compaction: Manchester United maintained a compact 28-meter vertical distance between their forward line and defensive back four. Casemiro and Kobbie Mainoo positioned themselves aggressively in the half-spaces, denying Ryan Gravenberch and Alexis Mac Allister central turn angles.\n\n2. Zone 14 Trap: When Liverpool center-backs attempted line-breaking passes into Zone 14, United triggered a 3-man synchronized closing trap. This produced 12 ball recoveries in the middle and attacking third.\n\n3. Transition Lethality: Within 3.2 seconds of regaining possession, United recorded shots on target, leading to goals by Bruno Fernandes (854s), Marcus Rashford (3505s), and Kobbie Mainoo (5335s).\n\nLimitations: Tracking coordinates are evaluated against normalized 0-100 pitch bounds; metrics strictly reflect observed candidate feed data.",
                'source': 'Microsoft Azure AI Foundry (gpt-4o-mini-tactical)',
                'timestamp': '88:55'
            }
        ],
        'matchContextSidebar': {
            'homeTeam': 'Manchester United',
            'awayTeam': 'Liverpool',
            'score': '3 - 3',
            'clock': '88:55',
            'dominantZone': 'Zone 14 (38% regains)',
            'highIntensityPressures': 42,
            'keyMoments': [
                {'clock': "14'", 'title': 'Bruno Fernandes Goal after Zone 14 Turnover'},
                {'clock': "27'", 'title': 'Mohamed Salah Equalizer from Half-Space Overload'},
                {'clock': "89'", 'title': 'Kobbie Mainoo Equalizing Wonder Goal'}
            ]
        }
    },
    'clubLogos': {
        'Manchester United': '/crests/manchester-united.svg',
        'Liverpool': '/crests/liverpool.svg',
        'Arsenal': '/crests/arsenal.svg',
        'Manchester City': '/crests/manchester-city.svg',
        'Chelsea': '/crests/chelsea.svg',
        'Tottenham': '/crests/tottenham.svg'
    }
}

with open('data/simulations/epl-intelligence-simulation.json', 'w') as f:
    json.dump(simulation_data, f, indent=2)

print('Updated data/simulations/epl-intelligence-simulation.json with Manchester United vs Liverpool!')
