using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Domain.Simulation;

public sealed record SyntheticPlayer(string Id, string Name, string TeamId, int ShirtNumber, string Role);
public sealed record SyntheticTeam(string Id, string Name, string Formation, IReadOnlyList<SyntheticPlayer> Players);
public sealed record PitchPosition(string PlayerId, string TeamId, double X, double Y);
public sealed record TrackingFrame(string Id, int ClockSeconds, double BallX, double BallY, IReadOnlyList<PitchPosition> Players);
public sealed record SimulationEvent(MatchEvent Event, string TeamId, string PlayerId, string Action, string Outcome, string? RelatedPlayerId = null);
public sealed record SyntheticFixture(string MatchId, string Version, int Seed, string Provenance, int DurationSeconds,
    int HalfTimeSeconds, double PitchLengthMetres, double PitchWidthMetres,
    SyntheticTeam Home, SyntheticTeam Away, IReadOnlyList<SimulationEvent> Events, IReadOnlyList<TrackingFrame> Frames)
{
    public string CoordinateConvention =>
        $"Offline synthetic fixture; metre coordinates, x=0..{PitchLengthMetres:R}, y=0..{PitchWidthMetres:R}; fixed stadium axes; attacking direction changes at {HalfTimeSeconds} seconds.";
}
public sealed record MatchSnapshot(string MatchId, int ClockSeconds, int Period, string Status, int HomeScore, int AwayScore,
    string Provenance, TrackingFrame Frame, long LastSequence, MatchPhase Phase, string? PossessionTeamId,
    int HomeActivePlayers, int AwayActivePlayers, IReadOnlyList<MatchPlayerState> Players, string CoordinateConvention,
    string? CompetitionId = null);
public sealed record TacticalInsight(string Id, string MatchId, int ClockSeconds, string Title, string Summary,
    string Classification, string DetectorVersion, IReadOnlyList<Guid> EventIds, IReadOnlyList<string> FrameIds);
public sealed record TacticalEvidence(TacticalInsight Insight, IReadOnlyList<SimulationEvent> Events, IReadOnlyList<TrackingFrame> Frames,
    string CoordinateConvention, IReadOnlyList<string> Conditions, IReadOnlyList<string> Limitations);
public sealed record MatchExplanation(string InsightId, int ClockSeconds, string Audience, string Source, string Text,
    IReadOnlyList<Guid> EventIds, IReadOnlyList<string> Limitations);

public static class FootballPitchDimensions
{
    public const double MinimumLengthMetres = 90;
    public const double MaximumLengthMetres = 120;
    public const double MinimumWidthMetres = 45;
    public const double MaximumWidthMetres = 90;

    public static bool AreValid(double lengthMetres, double widthMetres) =>
        double.IsFinite(lengthMetres) && lengthMetres is >= MinimumLengthMetres and <= MaximumLengthMetres &&
        double.IsFinite(widthMetres) && widthMetres is >= MinimumWidthMetres and <= MaximumWidthMetres;
}

public static class SyntheticFixtureGenerator
{
    public const string Version = "synthetic-v2";
    private const string DefinitionResource = "PitchMind.Domain.Simulation.synthetic-fixture-v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SyntheticFixture Generate(int seed = 42)
    {
        return GenerateFromDefinition(LoadDefinition(), seed);
    }

    internal static SyntheticFixture GenerateFromDefinitionJson(string json, int seed = 42)
    {
        var definition = JsonSerializer.Deserialize<FixtureDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException("Synthetic fixture definition is empty.");
        return GenerateFromDefinition(definition, seed);
    }

    private static SyntheticFixture GenerateFromDefinition(FixtureDefinition definition, int seed)
    {
        if (!string.Equals(definition.Version, Version, StringComparison.Ordinal))
            throw new InvalidOperationException($"Fixture definition version '{definition.Version}' does not match generator '{Version}'.");
        ValidateDefinition(definition);

        var homeDefinition = definition.Teams[0];
        var awayDefinition = definition.Teams[1];
        var home = ToTeam(homeDefinition);
        var away = ToTeam(awayDefinition);
        var fixtureMatchId = new MatchId(StableId($"{definition.FixtureId}:{definition.Version}:{seed}"));
        var events = new List<SimulationEvent>();

        void Add(int clock, int period, MatchEventType type, string team, string player, string action, string outcome, string? related = null)
        {
            var sequence = events.Count;
            var id = StableId($"{definition.FixtureId}:{definition.Version}:{seed}:{clock}:{sequence}:{type}:{action}");
            var occurredAt = definition.StartUtc.AddSeconds(clock);
            var matchEvent = MatchEvent.Create(id, fixtureMatchId, sequence, type, clock, period, occurredAt);
            events.Add(new(matchEvent, team, player, action, outcome, related));
        }

        foreach (var item in definition.ScenarioEvents)
            Add(item.ClockSeconds, item.Period, ParseEventType(item.Type), ResolveTeam(item.Team, home.Id, away.Id),
                item.PlayerId, item.Action, item.Outcome, item.RelatedPlayerId);

        for (var clock = definition.CirculationIntervalSeconds; clock < definition.DurationSeconds; clock += definition.CirculationIntervalSeconds)
        {
            if (clock == definition.HalfTimeSeconds || definition.SuppressedCirculationWindows.Any(w => clock >= w.StartSeconds && clock <= w.EndSeconds))
                continue;
            var teamDefinition = ((clock / definition.CirculationIntervalSeconds + seed) & 1) == 0 ? homeDefinition : awayDefinition;
            Add(clock, clock < definition.HalfTimeSeconds ? 1 : 2, MatchEventType.Pass, teamDefinition.Id,
                teamDefinition.CirculationPlayerId, "circulation", "complete", teamDefinition.CirculationReceiverId);
        }

        var ordered = events.OrderBy(e => e.Event.ClockSeconds).ThenBy(e => e.Event.Sequence)
            .Select((e, i) => e with { Event = e.Event with { Sequence = i } }).ToArray();
        var times = Enumerable.Range(0, definition.DurationSeconds / definition.CirculationIntervalSeconds + 1)
            .Select(i => i * definition.CirculationIntervalSeconds)
            .Concat(ordered.Select(e => e.Event.ClockSeconds))
            .Concat(definition.FrameOverrides.Select(f => f.ClockSeconds))
            .Distinct().Order().ToArray();
        var frames = times.Select(clock => Frame(clock, homeDefinition, awayDefinition, definition, seed)).ToArray();
        return new(fixtureMatchId.ToString(), definition.Version, seed, definition.Provenance, definition.DurationSeconds,
            definition.HalfTimeSeconds, definition.PitchLengthMetres, definition.PitchWidthMetres,
            home, away, Array.AsReadOnly(ordered), Array.AsReadOnly(frames));
    }

    private static FixtureDefinition LoadDefinition()
    {
        using var stream = typeof(SyntheticFixtureGenerator).Assembly.GetManifestResourceStream(DefinitionResource)
            ?? throw new InvalidOperationException($"Embedded fixture definition '{DefinitionResource}' was not found.");
        return JsonSerializer.Deserialize<FixtureDefinition>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Synthetic fixture definition is empty.");
    }

    private static void ValidateDefinition(FixtureDefinition definition)
    {
        if (definition.Teams is null || definition.ScenarioEvents is null || definition.SuppressedCirculationWindows is null ||
            definition.FrameOverrides is null || string.IsNullOrWhiteSpace(definition.FixtureId) || definition.Provenance != "synthetic" ||
            definition.DurationSeconds <= 0 || !FootballPitchDimensions.AreValid(definition.PitchLengthMetres, definition.PitchWidthMetres) ||
            definition.StartUtc == default || definition.StartUtc.Offset != TimeSpan.Zero ||
            definition.HalfTimeSeconds <= 0 || definition.HalfTimeSeconds >= definition.DurationSeconds ||
            definition.CirculationIntervalSeconds <= 0 || definition.DurationSeconds % definition.CirculationIntervalSeconds != 0 ||
            definition.Teams.Length != 2)
            throw new InvalidOperationException("Synthetic fixture definition has invalid match metadata.");

        if (definition.Teams.Any(t => t is null || t.Players is null))
            throw new InvalidOperationException("Synthetic fixture definition has invalid teams or player lists.");

        var teamIds = definition.Teams.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var allPlayers = definition.Teams.SelectMany(t => t.Players).ToArray();
        if (allPlayers.Any(p => p is null))
            throw new InvalidOperationException("Synthetic fixture definition contains a null player.");
        var playerIds = allPlayers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (teamIds.Count != 2 || playerIds.Count != allPlayers.Length || definition.Teams.Any(t => string.IsNullOrWhiteSpace(t.Id) ||
            string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Formation) || t.Players.Length < 11 ||
            !t.Players.Any(p => p.Id == t.CirculationPlayerId) || !t.Players.Any(p => p.Id == t.CirculationReceiverId)) ||
            allPlayers.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Role) ||
                p.ShirtNumber is < 1 or > 99 || !double.IsFinite(p.BaseX) || p.BaseX < 0 || p.BaseX > definition.PitchLengthMetres ||
                !double.IsFinite(p.BaseY) || p.BaseY < 0 || p.BaseY > definition.PitchWidthMetres || p.ActiveFromSeconds < 0 || p.ActiveFromSeconds > definition.DurationSeconds ||
                (p.ActiveUntilSeconds != int.MaxValue && p.ActiveUntilSeconds > definition.DurationSeconds) ||
                p.ActiveFromSeconds >= p.ActiveUntilSeconds))
            throw new InvalidOperationException("Synthetic fixture definition has invalid teams or circulation players.");

        foreach (var item in definition.ScenarioEvents)
        {
            if (item is null || item.ClockSeconds < 0 || item.ClockSeconds > definition.DurationSeconds || item.Period is < 1 or > 2 ||
                string.IsNullOrWhiteSpace(item.Type) || string.IsNullOrWhiteSpace(item.Action) || string.IsNullOrWhiteSpace(item.Outcome))
                throw new InvalidOperationException("Synthetic fixture definition has an invalid scenario event.");

            var validPeriod = item.ClockSeconds < definition.HalfTimeSeconds
                ? item.Period == 1
                : item.ClockSeconds > definition.HalfTimeSeconds
                    ? item.Period == 2
                    : (item.Period == 1 && item.Type == nameof(MatchEventType.HalfTime)) ||
                      (item.Period == 2 && item.Type == nameof(MatchEventType.KickOff));
            if (!validPeriod)
                throw new InvalidOperationException("Synthetic fixture event period does not match its match clock.");

            var type = ParseEventType(item.Type);
            var team = item.Team switch { "home" => definition.Teams[0], "away" => definition.Teams[1], "none" => null,
                _ => throw new InvalidOperationException("Synthetic fixture event has an unsupported team side.") };
            if (team is null ? !string.IsNullOrEmpty(item.PlayerId) : !team.Players.Any(p => p.Id == item.PlayerId))
                throw new InvalidOperationException("Synthetic fixture event references a player outside its team.");
            if (item.RelatedPlayerId is not null && !playerIds.Contains(item.RelatedPlayerId))
                throw new InvalidOperationException("Synthetic fixture event references an unknown related player.");

            if (type == MatchEventType.Substitution)
            {
                var outgoing = team?.Players.SingleOrDefault(p => p.Id == item.PlayerId);
                var incoming = team?.Players.SingleOrDefault(p => p.Id == item.RelatedPlayerId);
                if (outgoing is null || incoming is null || outgoing.ActiveUntilSeconds != item.ClockSeconds ||
                    incoming.ActiveFromSeconds != item.ClockSeconds)
                    throw new InvalidOperationException("Synthetic substitution must replace an active player with a same-team player at that time.");
            }
        }

        if (definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.KickOff)) != 2 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.HalfTime)) != 1 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.FullTime)) != 1 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.KickOff) && e.ClockSeconds == 0 && e.Period == 1) != 1 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.HalfTime) && e.ClockSeconds == definition.HalfTimeSeconds && e.Period == 1) != 1 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.KickOff) && e.ClockSeconds == definition.HalfTimeSeconds && e.Period == 2) != 1 ||
            definition.ScenarioEvents.Count(e => e.Type == nameof(MatchEventType.FullTime) && e.ClockSeconds == definition.DurationSeconds && e.Period == 2) != 1 ||
            definition.ScenarioEvents.Any(e => e.ClockSeconds == definition.DurationSeconds && e.Type != nameof(MatchEventType.FullTime)))
            throw new InvalidOperationException("Synthetic fixture must have exactly one kickoff per period, one halftime and one terminal full-time event.");

        if (definition.SuppressedCirculationWindows.Any(w => w is null || w.StartSeconds < 0 ||
                w.StartSeconds > w.EndSeconds || w.EndSeconds > definition.DurationSeconds) ||
            definition.FrameOverrides.Any(f => f is null || f.PlayerPositions is null) ||
            definition.FrameOverrides.Select(f => f.ClockSeconds).Distinct().Count() != definition.FrameOverrides.Length ||
            definition.FrameOverrides.Any(f => f.ClockSeconds < 0 || f.ClockSeconds > definition.DurationSeconds ||
            !double.IsFinite(f.BallX) || f.BallX < 0 || f.BallX > definition.PitchLengthMetres ||
            !double.IsFinite(f.BallY) || f.BallY < 0 || f.BallY > definition.PitchWidthMetres ||
            f.PlayerPositions.Any(p => !playerIds.Contains(p.Key) || p.Value is null || p.Value.Length != 2 || !double.IsFinite(p.Value[0]) || p.Value[0] < 0 ||
                p.Value[0] > definition.PitchLengthMetres || !double.IsFinite(p.Value[1]) || p.Value[1] < 0 || p.Value[1] > definition.PitchWidthMetres ||
                allPlayers.Single(player => player.Id == p.Key).ActiveFromSeconds > f.ClockSeconds ||
                f.ClockSeconds >= allPlayers.Single(player => player.Id == p.Key).ActiveUntilSeconds)))
            throw new InvalidOperationException("Synthetic fixture definition has an invalid frame override.");
    }

    private static SyntheticTeam ToTeam(TeamDefinition team) => new(team.Id, team.Name, team.Formation,
        Array.AsReadOnly(team.Players.Select(p => new SyntheticPlayer(p.Id, p.Name, team.Id, p.ShirtNumber, p.Role)).ToArray()));

    private static TrackingFrame Frame(int clock, TeamDefinition home, TeamDefinition away, FixtureDefinition definition, int seed)
    {
        var positions = new List<PitchPosition>();
        foreach (var team in new[] { home, away })
        foreach (var player in team.Players)
        {
            if (clock < player.ActiveFromSeconds || clock >= player.ActiveUntilSeconds) continue;
            var attacksRight = team.AttacksRightInPeriodOne == (clock < definition.HalfTimeSeconds);
            var x = attacksRight ? player.BaseX : definition.PitchLengthMetres - player.BaseX;
            var yJitter = (int)((uint)(seed ^ player.ShirtNumber ^ clock) % 3) - 1;
            var y = Math.Clamp(player.BaseY + yJitter, 0, definition.PitchWidthMetres);
            positions.Add(new(player.Id, team.Id, x, y));
        }

        var ballX = definition.PitchLengthMetres / 2;
        var ballY = definition.PitchWidthMetres / 2;
        var frameOverride = definition.FrameOverrides.SingleOrDefault(f => f.ClockSeconds == clock);
        if (frameOverride is not null)
        {
            ballX = frameOverride.BallX;
            ballY = frameOverride.BallY;
            foreach (var (playerId, point) in frameOverride.PlayerPositions)
            {
                var index = positions.FindIndex(p => p.PlayerId == playerId);
                if (index < 0) throw new InvalidOperationException($"Frame override references inactive player '{playerId}'.");
                positions[index] = positions[index] with { X = point[0], Y = point[1] };
            }
        }
        var frameId = StableId($"{definition.FixtureId}:{definition.Version}:{seed}:frame:{clock}").ToString("N");
        return new(frameId, clock, ballX, ballY, positions.AsReadOnly());
    }

    private static string ResolveTeam(string side, string homeId, string awayId) => side switch
    {
        "home" => homeId,
        "away" => awayId,
        "none" => string.Empty,
        _ => throw new InvalidOperationException($"Unsupported fixture team side '{side}'.")
    };

    private static MatchEventType ParseEventType(string type) => Enum.TryParse<MatchEventType>(type, ignoreCase: false, out var value)
        ? value
        : throw new InvalidOperationException($"Unsupported fixture event type '{type}'.");

    private static Guid StableId(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));

    private sealed class FixtureDefinition
    {
        public string Version { get; set; } = "";
        public string FixtureId { get; set; } = "";
        public string Provenance { get; set; } = "";
        public DateTimeOffset StartUtc { get; set; }
        public int DurationSeconds { get; set; }
        public int HalfTimeSeconds { get; set; }
        public int CirculationIntervalSeconds { get; set; }
        public double PitchLengthMetres { get; set; }
        public double PitchWidthMetres { get; set; }
        public TeamDefinition[] Teams { get; set; } = [];
        public ScenarioEventDefinition[] ScenarioEvents { get; set; } = [];
        public SuppressedWindow[] SuppressedCirculationWindows { get; set; } = [];
        public FrameOverride[] FrameOverrides { get; set; } = [];
    }

    private sealed class TeamDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Formation { get; set; } = "";
        public bool AttacksRightInPeriodOne { get; set; }
        public string CirculationPlayerId { get; set; } = "";
        public string CirculationReceiverId { get; set; } = "";
        public PlayerDefinition[] Players { get; set; } = [];
    }

    private sealed class PlayerDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int ShirtNumber { get; set; }
        public string Role { get; set; } = "";
        public double BaseX { get; set; }
        public double BaseY { get; set; }
        public int ActiveFromSeconds { get; set; }
        public int ActiveUntilSeconds { get; set; } = int.MaxValue;
    }

    private sealed class ScenarioEventDefinition
    {
        public int ClockSeconds { get; set; }
        public int Period { get; set; }
        public string Type { get; set; } = "";
        public string Team { get; set; } = "none";
        public string PlayerId { get; set; } = "";
        public string Action { get; set; } = "";
        public string Outcome { get; set; } = "";
        public string? RelatedPlayerId { get; set; }
    }

    private sealed class SuppressedWindow { public int StartSeconds { get; set; } public int EndSeconds { get; set; } }
    private sealed class FrameOverride
    {
        public int ClockSeconds { get; set; }
        public double BallX { get; set; }
        public double BallY { get; set; }
        public Dictionary<string, double[]> PlayerPositions { get; set; } = new();
    }
}
