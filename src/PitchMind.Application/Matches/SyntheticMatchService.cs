using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Application.Matches;

/// <summary>Offline fixture read model. Not a durable production event store.</summary>
public sealed class SyntheticMatchService
{
    public SyntheticFixture Fixture { get; }
    public SyntheticMatchService() : this(SyntheticFixtureGenerator.Generate()) { }
    public SyntheticMatchService(SyntheticFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (fixture.DurationSeconds <= 0 || fixture.HalfTimeSeconds <= 0 || fixture.HalfTimeSeconds >= fixture.DurationSeconds ||
            !FootballPitchDimensions.AreValid(fixture.PitchLengthMetres, fixture.PitchWidthMetres) ||
            fixture.Provenance != "synthetic" || fixture.Frames.Count == 0)
            throw new ArgumentException("A complete synthetic fixture with tracking is required.", nameof(fixture));
        if (fixture.Events.Count == 0 || fixture.Events.Select(e => e.Event.EventId).Distinct().Count() != fixture.Events.Count)
            throw new ArgumentException("Unique source events are required.", nameof(fixture));
        foreach (var entry in fixture.Events) MatchEventValidator.Validate(entry.Event);
        if (fixture.Events.Where((e, i) => e.Event.Sequence != i || e.Event.ClockSeconds > fixture.DurationSeconds || e.Event.MatchId.ToString() != fixture.MatchId).Any()
            || fixture.Events.Zip(fixture.Events.Skip(1)).Any(p => p.First.Event.ClockSeconds > p.Second.Event.ClockSeconds)
            || fixture.Events.Any(e => e.Event.ClockSeconds < fixture.HalfTimeSeconds && e.Event.Period != 1
                || e.Event.ClockSeconds > fixture.HalfTimeSeconds && e.Event.Period != 2
                || e.Event.ClockSeconds == fixture.HalfTimeSeconds && !(
                    e.Event.Type == MatchEventType.HalfTime && e.Event.Period == 1
                    || e.Event.Type == MatchEventType.KickOff && e.Event.Period == 2)))
            throw new ArgumentException("Ordered contiguous match events are required.", nameof(fixture));
        if (fixture.Frames.Any(f => f.ClockSeconds < 0 || f.ClockSeconds > fixture.DurationSeconds ||
                !double.IsFinite(f.BallX) || f.BallX < 0 || f.BallX > fixture.PitchLengthMetres ||
                !double.IsFinite(f.BallY) || f.BallY < 0 || f.BallY > fixture.PitchWidthMetres || f.Players.Any(p =>
                !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > fixture.PitchLengthMetres ||
                p.Y < 0 || p.Y > fixture.PitchWidthMetres)))
            throw new ArgumentException("Tracking must use finite in-pitch coordinates.", nameof(fixture));
        if (!fixture.Frames.Any(frame => frame.ClockSeconds == 0))
            throw new ArgumentException("A tracking frame at kickoff (elapsed second zero) is required to initialize the active roster.", nameof(fixture));
        Fixture = fixture with
        {
            Home = fixture.Home with { Players = Array.AsReadOnly(fixture.Home.Players.ToArray()) },
            Away = fixture.Away with { Players = Array.AsReadOnly(fixture.Away.Players.ToArray()) },
            Events = Array.AsReadOnly(fixture.Events.ToArray()),
            Frames = Array.AsReadOnly(fixture.Frames.OrderBy(f => f.ClockSeconds).Select(f => f with { Players = Array.AsReadOnly(f.Players.ToArray()) }).ToArray())
        };
    }

    public MatchSnapshot GetState(int clockSeconds)
    {
        ValidateTime(clockSeconds);
        var events = GetEvents(clockSeconds);
        var initialPlayers = Fixture.Home.Players.Concat(Fixture.Away.Players)
            .Select(player => (player.Id, player.TeamId)).ToArray();
        var activeAtKickoff = Fixture.Frames.First(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId).ToArray();
        var aggregate = new MatchAggregate(new(Guid.Parse(Fixture.MatchId)), Fixture.Home.Id, Fixture.Away.Id,
            initialPlayers, activeAtKickoff, Fixture.Provenance);
        foreach (var item in events) aggregate.Apply(item);
        var state = aggregate.Snapshot();
        var status = clockSeconds == Fixture.DurationSeconds ? "full-time" : "replay";
        return new(Fixture.MatchId, clockSeconds, state.Period, status, state.HomeScore, state.AwayScore, state.Provenance,
            Fixture.Frames.Last(f => f.ClockSeconds <= clockSeconds), state.LastSequence,
            state.Phase, state.PossessionTeamId, state.HomeActivePlayers, state.AwayActivePlayers, state.Players, Fixture.CoordinateConvention,
            state.CompetitionId);
    }
    public IReadOnlyList<SimulationEvent> GetEvents(int clockSeconds)
    {
        ValidateTime(clockSeconds);
        return Array.AsReadOnly(Fixture.Events.Where(e => e.Event.ClockSeconds <= clockSeconds).ToArray());
    }
    public IReadOnlyList<TacticalInsight> GetInsights(int clockSeconds)
    {
        ValidateTime(clockSeconds);
        return PressingSequenceDetector.Detect(Fixture, clockSeconds);
    }
    public TacticalEvidence? GetEvidence(string insightId, int clockSeconds)
    {
        var insight = GetInsights(clockSeconds).SingleOrDefault(i => i.Id == insightId);
        if (insight is null) return null;
        return new(insight, Fixture.Events.Where(e => insight.EventIds.Contains(e.Event.EventId)).ToArray(),
            Fixture.Frames.Where(f => insight.FrameIds.Contains(f.Id)).ToArray(),
            Fixture.CoordinateConvention,
            new[] { "Back-pass received by goalkeeper", "Opponent within 3 metres at press arrival", "Incomplete pressured pass within 10 seconds", "Same pressing team regains within 10 seconds", "Cutback then shot and goal within 15 seconds of regain" },
            new[] { "Synthetic scenario; thresholds are uncalibrated demonstration rules.", "Sequence supports a pressing interpretation, not proof of player or manager intent.", "Routine frames sampled every 30 seconds; do not treat gaps as observed movement." });
    }
    public MatchExplanation? Explain(string insightId, int clockSeconds, string audience = "fan")
    {
        if (audience is not ("fan" or "analyst")) throw new ArgumentException("Audience must be fan or analyst.", nameof(audience));
        var evidence = GetEvidence(insightId, clockSeconds);
        if (evidence is null) return null;
        var team = evidence.Events[1].TeamId == Fixture.Home.Id ? Fixture.Home.Name : Fixture.Away.Name;
        var opponent = evidence.Events[0].TeamId == Fixture.Home.Id ? Fixture.Home.Name : Fixture.Away.Name;
        static string Time(int seconds) => $"{seconds / 60:D2}:{seconds % 60:D2}";
        var text = audience == "analyst"
            ? $"At {Time(evidence.Events[0].Event.ClockSeconds)} {opponent} played back to the goalkeeper. {team}'s forward was within 3 metres at {Time(evidence.Events[1].Event.ClockSeconds)}. The subsequent pass was incomplete, {team} regained at {Time(evidence.Events[3].Event.ClockSeconds)}, cut back at {Time(evidence.Events[4].Event.ClockSeconds)} and scored at {Time(evidence.Events[6].Event.ClockSeconds)}. This observed sequence is consistent with a successful press; intent and causality beyond the sequence are unknown."
            : $"{team} closed down {opponent}'s goalkeeper after a back-pass, won the next ball and turned a cutback into a goal. The linked events show the sequence; they do not tell us what the coach intended.";
        return new(insightId, clockSeconds, audience, "deterministic-fallback", text, evidence.Insight.EventIds, evidence.Limitations);
    }
    private void ValidateTime(int clockSeconds)
    {
        if (clockSeconds < 0 || clockSeconds > Fixture.DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(clockSeconds), $"Clock must be between 0 and {Fixture.DurationSeconds} seconds.");
    }
}

public static class PressingSequenceDetector
{
    public const string Version = "press-sequence-v1";
    public static IReadOnlyList<TacticalInsight> Detect(SyntheticFixture fixture, int selectedClock)
    {
        var available = fixture.Events.Where(e => e.Event.ClockSeconds <= selectedClock).ToArray();
        var results = new List<TacticalInsight>();
        foreach (var trigger in available.Where(e => e.Action == "back-pass" && e.Outcome == "complete"))
        {
            var chain = available.Where(e => e.Event.ClockSeconds >= trigger.Event.ClockSeconds && e.Event.ClockSeconds <= trigger.Event.ClockSeconds + 25).ToArray();
            SimulationEvent? Find(string action) => chain.FirstOrDefault(e => e.Action == action);
            var press = Find("press-arrival"); var response = Find("pressured-pass"); var regain = Find("high-regain");
            var cutback = Find("cutback"); var shot = Find("shot"); var goal = Find("goal");
            if (press is null || response is null || regain is null || cutback is null || shot is null || goal is null) continue;
            var ordered = new[] { trigger, press, response, regain, cutback, shot, goal };
            if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Event.ClockSeconds >= pair.Second.Event.ClockSeconds)) continue;
            if (ordered.Any(e => e.Event.Period != trigger.Event.Period)) continue;
            if (press.TeamId == trigger.TeamId || response.TeamId != trigger.TeamId || response.Outcome != "incomplete" || regain.Outcome != "won") continue;
            if (press.RelatedPlayerId != trigger.RelatedPlayerId || response.PlayerId != trigger.RelatedPlayerId) continue;
            if (new[] { regain, cutback, shot, goal }.Any(e => e.TeamId != press.TeamId) || goal.Outcome != "scored") continue;
            if (response.Event.ClockSeconds - press.Event.ClockSeconds > 10 || regain.Event.ClockSeconds - response.Event.ClockSeconds > 10 || goal.Event.ClockSeconds - regain.Event.ClockSeconds > 15) continue;
            var frames = fixture.Frames.Where(f => ordered.Any(e => e.Event.ClockSeconds == f.ClockSeconds)).ToArray();
            var arrival = frames.SingleOrDefault(f => f.ClockSeconds == press.Event.ClockSeconds);
            var attacker = arrival?.Players.SingleOrDefault(p => p.PlayerId == press.PlayerId);
            var keeper = arrival?.Players.SingleOrDefault(p => p.PlayerId == press.RelatedPlayerId);
            if (frames.Length != ordered.Length || attacker is null || keeper is null) continue;
            if (trigger.Event.Type != MatchEventType.Pass || press.Event.Type != MatchEventType.Pressure || response.Event.Type != MatchEventType.Pass
                || regain.Event.Type != MatchEventType.Turnover || cutback.Event.Type != MatchEventType.Pass || shot.Event.Type != MatchEventType.Shot || goal.Event.Type != MatchEventType.Goal) continue;
            var distance = Math.Sqrt(Math.Pow(attacker.X - keeper.X, 2) + Math.Pow(attacker.Y - keeper.Y, 2));
            if (!double.IsFinite(distance) || distance > 3) continue;
            results.Add(new($"press-{trigger.Event.EventId:D}", fixture.MatchId, goal.Event.ClockSeconds,
                "Press followed by regain and goal", $"{(press.TeamId == fixture.Home.Id ? fixture.Home.Name : fixture.Away.Name)}'s press is followed by a high regain, cutback and goal.",
                "evidence-supported-pattern", Version, Array.AsReadOnly(ordered.Select(e => e.Event.EventId).ToArray()), Array.AsReadOnly(frames.Select(f => f.Id).ToArray())));
        }
        return results.AsReadOnly();
    }
}
