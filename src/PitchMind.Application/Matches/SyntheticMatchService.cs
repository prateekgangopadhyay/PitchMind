using PitchMind.Domain.Events;
using PitchMind.Domain.Analytics;
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
        var state = ProjectMatchState(clockSeconds, events);
        var status = clockSeconds == Fixture.DurationSeconds ? "full-time" : "replay";
        return new(Fixture.MatchId, clockSeconds, state.Period, status, state.HomeScore, state.AwayScore, state.Provenance,
            Fixture.Frames.Last(f => f.ClockSeconds <= clockSeconds), state.LastSequence,
            state.Phase, state.PossessionTeamId, state.HomeActivePlayers, state.AwayActivePlayers, state.Players, Fixture.CoordinateConvention);
    }

    private MatchState ProjectMatchState(int clockSeconds, IReadOnlyList<SimulationEvent> events)
    {
        var initialPlayers = Fixture.Home.Players.Concat(Fixture.Away.Players)
            .Select(player => (player.Id, player.TeamId)).ToArray();
        var activeAtKickoff = Fixture.Frames.First(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId).ToArray();
        var aggregate = new MatchAggregate(new(Guid.Parse(Fixture.MatchId)), Fixture.Home.Id, Fixture.Away.Id,
            initialPlayers, activeAtKickoff, Fixture.Provenance);
        foreach (var item in events) aggregate.Apply(item);
        return aggregate.Snapshot() with { ClockSeconds = clockSeconds };
    }
    public IReadOnlyList<SimulationEvent> GetEvents(int clockSeconds)
    {
        ValidateTime(clockSeconds);
        return Array.AsReadOnly(Fixture.Events.Where(e => e.Event.ClockSeconds <= clockSeconds).ToArray());
    }
    public IReadOnlyList<TacticalInsight> GetInsights(int clockSeconds)
    {
        ValidateTime(clockSeconds);
        if (Fixture.Frames.Count == 0) return Array.Empty<TacticalInsight>();
        var events = GetEvents(clockSeconds);
        var input = new PressSequenceInput(
            Fixture.MatchId,
            Fixture.Version,
            Fixture.Provenance,
            null,
            clockSeconds,
            events.Count == 0 ? -1 : events[^1].Event.Sequence,
            Fixture.Home.Id,
            Fixture.Home.Name,
            Fixture.Away.Id,
            Fixture.Away.Name,
            "stadium-metres",
            Fixture.PitchLengthMetres,
            Fixture.PitchWidthMetres,
            events,
            Array.AsReadOnly(Fixture.Frames.Where(frame => frame.ClockSeconds <= clockSeconds).ToArray()))
        {
            SelectedState = ProjectMatchState(clockSeconds, events),
            SourceSha256 = Fixture.SourceSha256
        };
        return PressSequenceDetector.Detect(input);
    }
    public IReadOnlyList<KeyMoment> GetKeyMoments(int clockSeconds, KeyMomentPolicy? policy = null)
    {
        ValidateTime(clockSeconds);
        var events = GetEvents(clockSeconds);
        var throughSequence = events.Count == 0 ? -1 : events[^1].Event.Sequence;
        return KeyMomentGenerator.Generate(Fixture.MatchId, Fixture.Version, Fixture.Provenance, null,
            clockSeconds, throughSequence, Fixture.Home.Id, Fixture.Home.Name, Fixture.Away.Id, Fixture.Away.Name,
            events, GetInsights(clockSeconds), policy);
    }
    public TacticalEvidence? GetEvidence(string insightId, int clockSeconds)
    {
        var insight = GetInsights(clockSeconds).SingleOrDefault(i => i.Id == insightId);
        if (insight is null) return null;
        return new(insight, Fixture.Events.Where(e => insight.EventIds.Contains(e.Event.EventId)).ToArray(),
            Fixture.Frames.Where(f => insight.FrameIds.Contains(f.Id)).ToArray(),
            Fixture.CoordinateConvention,
            insight.Conditions,
            new[] { "Synthetic-only candidate; the detector policy is unapproved and publishable is false.", "The sequence does not establish causality or managerial intent.", "Routine frames are sparse; do not infer continuous movement or closing speed." });
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
            ? $"In this synthetic fixture, {opponent} played back at {Time(evidence.Events[0].Event.ClockSeconds)}. A {team} pressure event followed, then an incomplete pass, regain, cutback, shot and goal. This is a simulated sequence candidate under an unapproved demonstration policy; it does not establish causality or intent."
            : $"This synthetic fixture contains a {team} pressure event followed by an opponent pass, a regain and a shot sequence. It is a simulated candidate, not proof of tactical intent or causality.";
        return new(insightId, clockSeconds, audience, "deterministic-fallback", text, evidence.Insight.EventIds, evidence.Limitations);
    }
    private void ValidateTime(int clockSeconds)
    {
        if (clockSeconds < 0 || clockSeconds > Fixture.DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(clockSeconds), $"Clock must be between 0 and {Fixture.DurationSeconds} seconds.");
    }
}
