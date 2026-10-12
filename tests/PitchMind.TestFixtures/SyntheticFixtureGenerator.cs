using System.Security.Cryptography;
using System.Text;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.TestFixtures;

/// <summary>Builds deterministic, fictional golden data for automated tests only.</summary>
public static class SyntheticFixtureGenerator
{
    public const string Version = "synthetic-v1";
    public static readonly MatchId FixtureId = new(new Guid("876855d7-86d3-4a34-9b51-2dd8c9799a91"));

    public static SyntheticFixture Generate(int seed = 42)
    {
        var roles = new[] { "GK", "RB", "CB", "CB", "LB", "CM", "CM", "AM", "RW", "ST", "LW" };
        SyntheticTeam Team(string id, string name) => new(id, name, "4-2-3-1", Array.AsReadOnly(roles.Select((role, i) =>
            new SyntheticPlayer($"{id}-{i + 1}", $"{name.Split(' ')[0]} Player {i + 1}", id, i + 1, role)).ToArray()));
        var home = Team("harbour", "Harbour FC");
        var away = Team("ridge", "Ridge Athletic");
        var events = new List<SimulationEvent>();
        void Add(int clock, MatchEventType type, string team, string player, string action, string outcome, string? related = null)
        {
            var sequence = events.Count;
            var id = StableId($"{Version}:{seed}:{clock}:{sequence}:{action}");
            var evt = MatchEvent.Create(id, FixtureId, sequence, type, clock, clock < 2700 ? 1 : 2,
                new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero).AddSeconds(clock));
            events.Add(new(evt, team, player, action, outcome, related));
        }

        Add(0, MatchEventType.KickOff, home.Id, "harbour-10", "kick-off", "complete");
        for (var clock = 30; clock < 5400; clock += 30)
        {
            if (clock is >= 3290 and <= 3330) continue;
            var isHome = (clock / 30 + seed) % 2 == 0;
            var team = isHome ? home.Id : away.Id;
            Add(clock, MatchEventType.Pass, team, $"{team}-6", "circulation", "complete", $"{team}-7");
        }

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
        return new(FixtureId.ToString(), Version, seed, "synthetic", 5400, home, away,
            Array.AsReadOnly(ordered), Array.AsReadOnly(frames));
    }

    private static TrackingFrame Frame(int clock, SyntheticTeam home, SyntheticTeam away, int seed)
    {
        var positions = new List<PitchPosition>();
        foreach (var team in new[] { home, away })
        foreach (var player in team.Players)
        {
            var attacksRight = (team.Id == home.Id) == (clock < 2700);
            var x = player.ShirtNumber == 1 ? 5 : 20 + (player.ShirtNumber / 3) * 16;
            var y = 8 + ((player.ShirtNumber * 13 + seed) % 52);
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
