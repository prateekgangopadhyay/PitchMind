using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.TestFixtures;

/// <summary>Builds deterministic, fictional golden data for automated tests only.</summary>
public static class SyntheticFixtureGenerator
{
    public const string Version = "synthetic-v3";
    public static MatchId FixtureId => MatchIdForSeed(42);

    public static SyntheticFixture Generate(int seed = 42)
    {
        var matchId = MatchIdForSeed(seed);
        var roles = new[] { "GK", "RB", "CB", "CB", "LB", "CM", "CM", "AM", "RW", "ST", "LW" };
        SyntheticTeam Team(string id, string name) => new(id, name, "4-2-3-1", Array.AsReadOnly(roles.Select((role, i) =>
            new SyntheticPlayer($"{id}-{i + 1}", $"{name.Split(' ')[0]} Player {i + 1}", id, i + 1, role)).ToArray()));
        var home = Team("harbour", "Harbour FC");
        var away = Team("ridge", "Ridge Athletic");
        var events = new List<SimulationEvent>();
        void Add(int clock, MatchEventType type, string team, string player, string action, string outcome,
            string? related = null, int? period = null)
        {
            var sequence = events.Count;
            var id = StableId($"{Version}:{seed.ToString(CultureInfo.InvariantCulture)}:{clock}:{sequence}:{action}");
            var evt = MatchEvent.Create(id, matchId, sequence, type, clock, period ?? (clock < 2700 ? 1 : 2),
                new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero).AddSeconds(clock));
            events.Add(new(evt, team, player, action, outcome, related));
        }

        Add(0, MatchEventType.KickOff, home.Id, "harbour-10", "kick-off", "complete");
        for (var clock = 30; clock < 5400; clock += 30)
        {
            if (clock is 2700 or >= 3290 and <= 3330) continue;
            // Keep a side in possession for two consecutive circulation events rather than
            // manufacturing a turnover on every pass. The authored scenario phase annotations
            // use the same 60-second blocks except where the pressing sequence changes control.
            var isHome = (clock / 60 + seed) % 2 == 0;
            var team = isHome ? home.Id : away.Id;
            Add(clock, MatchEventType.Pass, team, $"{team}-6", "circulation", "complete", $"{team}-7");
        }

        Add(2700, MatchEventType.HalfTime, "", "", "half-time", "complete", period: 1);
        Add(2700, MatchEventType.KickOff, away.Id, "ridge-10", "kick-off", "complete", period: 2);
        Add(3290, MatchEventType.Pass, away.Id, "ridge-3", "back-pass", "complete", "ridge-1");
        Add(3293, MatchEventType.Pressure, home.Id, "harbour-10", "press-arrival", "pressure", "ridge-1");
        Add(3296, MatchEventType.Pass, away.Id, "ridge-1", "pressured-pass", "incomplete", "ridge-2");
        Add(3299, MatchEventType.Turnover, home.Id, "harbour-11", "high-regain", "won", "ridge-2");
        Add(3304, MatchEventType.Pass, home.Id, "harbour-11", "cutback", "complete", "harbour-10");
        Add(3308, MatchEventType.Shot, home.Id, "harbour-10", "shot", "on-target");
        Add(3309, MatchEventType.Goal, home.Id, "harbour-10", "goal", "scored");
        Add(4680, MatchEventType.Goal, away.Id, "ridge-10", "goal", "scored");
        Add(5400, MatchEventType.FullTime, "", "", "full-time", "complete");

        var ordered = events.OrderBy(e => e.Event.ClockSeconds).ThenBy(e => e.Event.Sequence)
            .Select((e, i) => e with { Event = e.Event with { Sequence = i } }).ToArray();
        var times = Enumerable.Range(0, 181).Select(i => i * 30)
            .Concat(new[] { 3290, 3293, 3296, 3299, 3304, 3308, 3309 }).Distinct().Order().ToArray();
        var frames = times.Select(clock => Frame(clock, home, away, seed)).ToArray();
        return new(matchId.ToString(), Version, seed, "synthetic", 5400, 2700, 105, 68, home, away,
            Array.AsReadOnly(ordered), Array.AsReadOnly(frames));
    }

    /// <summary>
    /// Adds test-only disciplinary, substitution, phase and lineup annotations around the
    /// deterministic football fixture. These annotations are not canonical telemetry contracts.
    /// </summary>
    public static GovernedSyntheticScenario GenerateScenario(int seed = 42)
    {
        var fixture = Generate(seed);
        var substitutes = new[]
        {
            new SyntheticPlayer("harbour-12", "Harbour Player 12", fixture.Home.Id, 12, "CM"),
            new SyntheticPlayer("ridge-12", "Ridge Player 12", fixture.Away.Id, 12, "CM")
        };
        var squad = fixture.Home.Players.Concat(fixture.Away.Players).Concat(substitutes).ToArray();
        var actions = new[]
        {
            Action(seed, 1280, SyntheticScenarioEventKind.YellowCard, fixture.Away.Id, "ridge-4", outcome: "cautioned"),
            Action(seed, 3600, SyntheticScenarioEventKind.Substitution, fixture.Home.Id, "harbour-7", "harbour-12", "completed"),
            Action(seed, 3900, SyntheticScenarioEventKind.Substitution, fixture.Away.Id, "ridge-11", "ridge-12", "completed"),
            Action(seed, 4200, SyntheticScenarioEventKind.RedCard, fixture.Away.Id, "ridge-3", outcome: "dismissed")
        };
        var fixtureEvents = fixture.Events.Select(entry => new SyntheticScenarioEvent(
            entry.Event.EventId, 0, entry.Event.Sequence, entry.Event.ClockSeconds, entry.Event.Period,
            Enum.Parse<SyntheticScenarioEventKind>(entry.Event.Type.ToString()), entry.TeamId, entry.PlayerId,
            entry.RelatedPlayerId, entry.Outcome));
        var events = fixtureEvents.Concat(actions)
            .OrderBy(entry => entry.ClockSeconds)
            .ThenBy(entry => entry.SourceSequence.HasValue ? 0 : 1)
            .ThenBy(entry => entry.SourceSequence ?? long.MaxValue)
            .ThenBy(entry => entry.EventId)
            .Select((entry, sequence) => entry with { Sequence = sequence })
            .ToArray();
        var possessionPhases = BuildPossessionPhases(seed, fixture);
        var frames = fixture.Frames.Select(frame => ApplyActiveRoster(frame, events)).ToArray();
        var frozenSquad = Array.AsReadOnly(squad);
        var frozenEvents = Array.AsReadOnly(events);
        var frozenPhases = Array.AsReadOnly(possessionPhases);
        var frozenFrames = Array.AsReadOnly(frames);
        var originalEventsById = fixture.Events.ToDictionary(entry => entry.Event.EventId);
        var matchId = new MatchId(Guid.Parse(fixture.MatchId));
        var scenarioFixtureEvents = events.Select(entry =>
        {
            var occurredAt = originalEventsById.TryGetValue(entry.EventId, out var source)
                ? source.Event.OccurredAt
                : new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero).AddSeconds(entry.ClockSeconds);
            var domainEvent = MatchEvent.Create(entry.EventId, matchId, entry.Sequence,
                Enum.Parse<MatchEventType>(entry.Type.ToString()), entry.ClockSeconds, entry.Period, occurredAt);
            return new SimulationEvent(domainEvent, entry.TeamId, entry.PlayerId,
                entry.Type.ToString(), entry.Outcome ?? string.Empty, entry.RelatedPlayerId);
        }).ToArray();
        var scenarioFixture = fixture with
        {
            Home = fixture.Home with { Players = Array.AsReadOnly(fixture.Home.Players.Append(substitutes[0]).ToArray()) },
            Away = fixture.Away with { Players = Array.AsReadOnly(fixture.Away.Players.Append(substitutes[1]).ToArray()) },
            Events = Array.AsReadOnly(scenarioFixtureEvents),
            Frames = frozenFrames
        };
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            scenarioFixture,
            squad = frozenSquad,
            events = frozenEvents,
            possessionPhases = frozenPhases,
            frames = frozenFrames
        });
        var digest = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        return new(scenarioFixture, frozenSquad, frozenEvents, frozenPhases, frozenFrames, digest);
    }

    /// <summary>Creates named negative controls for the authored pressing golden.</summary>
    public static IReadOnlyList<SyntheticPressCounterexample> GeneratePressCounterexamples(int seed = 42)
    {
        var fixture = Generate(seed);
        var missingTracking = fixture with { Frames = Array.Empty<TrackingFrame>() };
        var completedPass = fixture with
        {
            Events = Array.AsReadOnly(fixture.Events.Select(entry => entry.Action == "pressured-pass"
                ? entry with { Outcome = "complete" }
                : entry).ToArray())
        };
        var wrongTeamRegain = fixture with
        {
            Events = Array.AsReadOnly(fixture.Events.Select(entry => entry.Action == "high-regain"
                ? entry with { TeamId = fixture.Away.Id }
                : entry).ToArray())
        };
        var distantArrival = fixture with
        {
            Frames = Array.AsReadOnly(fixture.Frames.Select(frame => frame.ClockSeconds == 3293
                ? frame with
                {
                    Players = Array.AsReadOnly(frame.Players.Select(player => player.PlayerId == "harbour-10"
                        ? player with { X = 40 }
                        : player).ToArray())
                }
                : frame).ToArray())
        };

        return Array.AsReadOnly(new[]
        {
            new SyntheticPressCounterexample(SyntheticPressCounterexampleKind.MissingTracking, "missing-tracking", missingTracking),
            new SyntheticPressCounterexample(SyntheticPressCounterexampleKind.CompletedOpponentPass, "completed-opponent-pass", completedPass),
            new SyntheticPressCounterexample(SyntheticPressCounterexampleKind.WrongTeamRegain, "wrong-team-regain", wrongTeamRegain),
            new SyntheticPressCounterexample(SyntheticPressCounterexampleKind.DistantPressArrival, "distant-press-arrival", distantArrival)
        });
    }

    private static SyntheticScenarioEvent Action(int seed, int clock, SyntheticScenarioEventKind kind, string team,
        string player, string? related = null, string? outcome = null)
    {
        var sequenceKey = related is null ? player : $"{player}:{related}";
        var kindName = kind.ToString();
        var id = StableId($"{Version}:{seed.ToString(CultureInfo.InvariantCulture)}:{clock}:{kindName}:{team}:{sequenceKey}");
        return new(id, 0, null, clock, clock < 2700 ? 1 : 2, kind, team, player, related, outcome);
    }

    private static SyntheticPossessionPhase[] BuildPossessionPhases(int seed, SyntheticFixture fixture)
    {
        var homeId = fixture.Home.Id;
        var awayId = fixture.Away.Id;
        var phases = new List<SyntheticPossessionPhase>();
        for (var start = 0; start < 5400; start += 60)
        {
            var end = Math.Min(start + 60, 5400);
            var team = ((start / 60 + seed) % 2 == 0) ? homeId : awayId;
            var phase = team == homeId ? "build-up" : "settled-possession";
            phases.Add(new(start, end, start < 2700 ? 1 : 2, team, phase));
        }

        // The counterpress sequence is explicitly represented as an authored possession change.
        var index = phases.FindIndex(p => p.StartClockSeconds == 3240);
        phases.RemoveAll(p => p.StartClockSeconds is >= 3240 and < 3360);
        phases.InsertRange(index, new[]
        {
            new SyntheticPossessionPhase(3240, 3290, 2,
                fixture.Events.Single(e => e.Event.ClockSeconds == 3270 && e.Action == "circulation").TeamId,
                "settled-possession"),
            new SyntheticPossessionPhase(3290, 3299, 2, awayId, "opponent-build-up"),
            new SyntheticPossessionPhase(3299, 3304, 2, homeId, "high-regain-transition"),
            new SyntheticPossessionPhase(3304, 3360, 2, homeId, "attacking-exploitation")
        });
        return phases.ToArray();
    }

    private static TrackingFrame ApplyActiveRoster(TrackingFrame frame, IReadOnlyList<SyntheticScenarioEvent> events)
    {
        var active = frame.Players.ToList();
        foreach (var action in events.Where(a => a.Type == SyntheticScenarioEventKind.Substitution && a.ClockSeconds <= frame.ClockSeconds))
        {
            var outgoing = active.Single(p => p.PlayerId == action.PlayerId);
            active.Remove(outgoing);
            active.Add(outgoing with { PlayerId = action.RelatedPlayerId! });
        }

        foreach (var dismissal in events.Where(a => a.Type == SyntheticScenarioEventKind.RedCard && a.ClockSeconds <= frame.ClockSeconds))
            active.RemoveAll(p => p.PlayerId == dismissal.PlayerId);

        return frame with { Players = Array.AsReadOnly(active.ToArray()) };
    }

    private static MatchId MatchIdForSeed(int seed) => new(StableId($"{Version}:{seed.ToString(CultureInfo.InvariantCulture)}:match"));

    private static TrackingFrame Frame(int clock, SyntheticTeam home, SyntheticTeam away, int seed)
    {
        var seedOffset = (int)(((long)seed % 52 + 52) % 52);
        var positions = new List<PitchPosition>();
        foreach (var team in new[] { home, away })
        foreach (var player in team.Players)
        {
            var attacksRight = (team.Id == home.Id) == (clock < 2700);
            var x = player.ShirtNumber == 1 ? 5 : 20 + (player.ShirtNumber / 3) * 16;
            var y = 8 + ((player.ShirtNumber * 13 + seedOffset) % 52);
            positions.Add(new(player.Id, team.Id, attacksRight ? x : 105 - x, y));
        }

        var tactical = clock is >= 3290 and <= 3309;
        if (tactical)
        {
            for (var i = 0; i < positions.Count; i++)
                positions[i] = positions[i].PlayerId switch
                {
                    "ridge-1" => positions[i] with { X = 5, Y = 34 },
                    "ridge-2" => positions[i] with { X = 15, Y = 12 },
                    "harbour-10" => positions[i] with { X = clock < 3293 ? 10 : 7, Y = 34 },
                    "harbour-11" => positions[i] with { X = 14, Y = 13 },
                    _ => positions[i]
                };
        }

        var ballX = tactical ? (clock <= 3296 ? 5 : clock <= 3304 ? 14 : 7) : 52.5;
        var ballY = tactical && clock is >= 3299 and <= 3304 ? 13 : 34;
        return new($"frame-{clock}", clock, ballX, ballY, positions.AsReadOnly());
    }

    private static Guid StableId(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}
