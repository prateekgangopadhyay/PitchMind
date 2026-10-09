using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Analytics;

/// <summary>The exact offline demonstration rule. It is versioned but not approved for football publication.</summary>
public sealed class PressSequencePolicy
{
    private PressSequencePolicy() { }

    public static PressSequencePolicy UnapprovedDemoV2 { get; } = new();
    public string Version => "press-sequence-v2";
    public string ReviewStatus => "unapproved-demo";
    public int MaximumTriggerToOutcomeSeconds => 25;
    public int MaximumPressToResponseSeconds => 10;
    public int MaximumResponseToRegainSeconds => 10;
    public int MaximumRegainToOutcomeSeconds => 15;
    public double MaximumPressureDistanceMetres => 3;
    public bool Publishable => false;
}

/// <summary>Immutable, selected-time input for a deterministic mechanism candidate.</summary>
public sealed record PressSequenceInput(
    string MatchId,
    string SourceVersion,
    string Provenance,
    int? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    string HomeTeamId,
    string HomeTeamName,
    string AwayTeamId,
    string AwayTeamName,
    string CoordinateSystem,
    double PitchLengthMetres,
    double PitchWidthMetres,
    IReadOnlyList<SimulationEvent> Events,
    IReadOnlyList<TrackingFrame> Frames)
{
    public MatchState? SelectedState { get; init; }
}

/// <summary>
/// Detects one specific synthetic press sequence. The unapproved demo policy and its result
/// cannot be represented as a publishable or canonical football fact.
/// </summary>
public static class PressSequenceDetector
{
    public static IReadOnlyList<TacticalInsight> Detect(PressSequenceInput input,
        PressSequencePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        policy ??= PressSequencePolicy.UnapprovedDemoV2;
        Validate(input, policy);

        // Spatial distances are metres only when the fixture declares stadium metres.
        if (input.CoordinateSystem != "stadium-metres" || input.Events.Count == 0 || input.Frames.Count == 0)
            return Array.Empty<TacticalInsight>();

        var framesByClock = input.Frames.ToDictionary(frame => frame.ClockSeconds);
        var detections = new List<TacticalInsight>();
        foreach (var trigger in input.Events.Where(item => item.Event.Type == MatchEventType.Pass &&
                     item.Action == "back-pass" && item.Outcome == "complete"))
        {
            var pressure = FindNext(input, trigger, "press-arrival", policy.MaximumTriggerToOutcomeSeconds);
            if (pressure is null || pressure.Event.Type != MatchEventType.Pressure ||
                pressure.TeamId == trigger.TeamId || pressure.RelatedPlayerId != trigger.RelatedPlayerId)
                continue;

            var response = FindNext(input, pressure, "pressured-pass", policy.MaximumPressToResponseSeconds);
            if (response is null || response.Event.Type != MatchEventType.Pass || response.TeamId != trigger.TeamId ||
                response.PlayerId != trigger.RelatedPlayerId || response.Outcome != "incomplete")
                continue;

            var regain = FindNext(input, response, "high-regain", policy.MaximumResponseToRegainSeconds);
            if (regain is null || regain.Event.Type != MatchEventType.Turnover || regain.TeamId != pressure.TeamId ||
                regain.Outcome != "won")
                continue;

            var cutback = FindNext(input, regain, "cutback", policy.MaximumRegainToOutcomeSeconds);
            if (cutback is null || cutback.Event.Type != MatchEventType.Pass || cutback.TeamId != pressure.TeamId ||
                cutback.Outcome != "complete")
                continue;

            var shot = FindNext(input, cutback, "shot", policy.MaximumRegainToOutcomeSeconds);
            if (shot is null || shot.Event.Type != MatchEventType.Shot || shot.TeamId != pressure.TeamId)
                continue;

            var goal = FindNext(input, shot, "goal", policy.MaximumRegainToOutcomeSeconds);
            if (goal is null || goal.Event.Type != MatchEventType.Goal || goal.TeamId != pressure.TeamId ||
                goal.Outcome != "scored" || goal.Event.ClockSeconds - regain.Event.ClockSeconds > policy.MaximumRegainToOutcomeSeconds ||
                goal.Event.ClockSeconds - trigger.Event.ClockSeconds > policy.MaximumTriggerToOutcomeSeconds)
                continue;

            var events = new[] { trigger, pressure, response, regain, cutback, shot, goal };
            if (events.Any(item => item.Event.Period != trigger.Event.Period) ||
                !events.All(item => item.Event.ClockSeconds <= input.SelectedTimeSeconds))
                continue;

            var requiredFrameClocks = events.Select(item => item.Event.ClockSeconds).Distinct().ToArray();
            if (requiredFrameClocks.Any(clock => !framesByClock.ContainsKey(clock))) continue;
            var pressureFrame = framesByClock[pressure.Event.ClockSeconds];
            var presser = pressureFrame.Players.SingleOrDefault(player => player.PlayerId == pressure.PlayerId &&
                player.TeamId == pressure.TeamId);
            var carrier = pressureFrame.Players.SingleOrDefault(player => player.PlayerId == pressure.RelatedPlayerId &&
                player.TeamId == trigger.TeamId);
            if (presser is null || carrier is null) continue;

            var distance = Math.Sqrt(Math.Pow(presser.X - carrier.X, 2) + Math.Pow(presser.Y - carrier.Y, 2));
            if (!double.IsFinite(distance) || distance > policy.MaximumPressureDistanceMetres) continue;

            var frameIds = requiredFrameClocks.Select(clock => framesByClock[clock].Id).ToArray();
            var pressingTeam = pressure.TeamId == input.HomeTeamId ? input.HomeTeamName : input.AwayTeamName;
            var isHomePressingTeam = pressure.TeamId == input.HomeTeamId;
            var teamActivePlayers = isHomePressingTeam
                ? input.SelectedState!.HomeActivePlayers : input.SelectedState!.AwayActivePlayers;
            var opponentActivePlayers = isHomePressingTeam
                ? input.SelectedState!.AwayActivePlayers : input.SelectedState!.HomeActivePlayers;
            var scoreline = $"{input.HomeTeamName} {input.SelectedState!.HomeScore}–{input.SelectedState.AwayScore} {input.AwayTeamName}";
            var context = new TacticalContext(scoreline, isHomePressingTeam ? "home" : "away",
                teamActivePlayers - opponentActivePlayers,
                "opponent in possession at trigger; defending after observed regain",
                input.SelectedState.Phase.ToString());
            var conditions = Array.AsReadOnly(new[]
            {
                $"trigger: completed back-pass event {trigger.Event.EventId:D} (sequence {trigger.Event.Sequence})",
                $"pressure: opposing pressure event {pressure.Event.EventId:D}; observed separation {distance:F2}m <= {policy.MaximumPressureDistanceMetres:F0}m",
                $"response: incomplete opponent pass {response.Event.EventId:D} within {response.Event.ClockSeconds - pressure.Event.ClockSeconds}s",
                $"regain: same pressing team turnover {regain.Event.EventId:D} within {regain.Event.ClockSeconds - response.Event.ClockSeconds}s",
                $"exploitation: completed cutback {cutback.Event.EventId:D} followed by shot {shot.Event.EventId:D}",
                $"outcome: goal event {goal.Event.EventId:D} at {goal.Event.ClockSeconds}s; sequence ends {goal.Event.ClockSeconds - trigger.Event.ClockSeconds}s after trigger",
                $"coverage: {requiredFrameClocks.Length} distinct event-time frames include the pressure participants",
                $"selected-time: all evidence is at or before {input.SelectedTimeSeconds}s; through sequence {input.ThroughSequence}",
                $"context: {scoreline}; pressing team role {context.TeamRole}; numerical advantage {context.NumericalAdvantage}; match phase {context.MatchPhase}; opponent goes from in-possession to defending after observed regain",
                $"policy: {policy.Version} ({policy.ReviewStatus}); publication approval is absent"
            });

            var insight = new TacticalInsight(
                $"press-{trigger.Event.EventId:D}", input.MatchId, goal.Event.ClockSeconds,
                "Synthetic pressure sequence candidate",
                $"{pressingTeam}'s observed pressure is followed by a regain, cutback, shot and goal in this synthetic fixture. At selected time the scoreline is {scoreline}; active-player difference for this team is {context.NumericalAdvantage:+#;-#;0}. The sequence describes observed events, not intent, control, or comparative tactical quality.",
                "simulated", policy.Version,
                Array.AsReadOnly(events.Select(item => item.Event.EventId).ToArray()),
                Array.AsReadOnly(frameIds))
            {
                SourceVersion = input.SourceVersion,
                CanonicalRevision = input.CanonicalRevision,
                SelectedTimeSeconds = input.SelectedTimeSeconds,
                ThroughSequence = input.ThroughSequence,
                PolicyStatus = policy.ReviewStatus,
                Publishable = policy.Publishable,
                Conditions = conditions,
                Context = context
            };
            var graph = TacticalEvidenceGraphBuilder.Build(insight, input.Events, input.Frames);
            if (insight.Publishable != graph.Publishable)
                throw new InvalidDataException("The detector claim cannot publish without validated canonical evidence.");
            detections.Add(insight with { EvidenceGraph = graph });
        }
        return detections.AsReadOnly();
    }

    private static SimulationEvent? FindNext(PressSequenceInput input, SimulationEvent after, string action,
        int maximumDelaySeconds)
    {
        return input.Events.FirstOrDefault(candidate => candidate.Event.Sequence > after.Event.Sequence &&
            candidate.Event.Period == after.Event.Period && candidate.Event.ClockSeconds >= after.Event.ClockSeconds &&
            candidate.Event.ClockSeconds - after.Event.ClockSeconds <= maximumDelaySeconds && candidate.Action == action);
    }

    private static void Validate(PressSequenceInput input, PressSequencePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(input.MatchId) || string.IsNullOrWhiteSpace(input.SourceVersion) ||
            input.Provenance != "synthetic" || input.SelectedTimeSeconds < 0 ||
            input.CanonicalRevision is <= 0 || string.IsNullOrWhiteSpace(input.HomeTeamId) ||
            string.IsNullOrWhiteSpace(input.AwayTeamId) || input.HomeTeamId == input.AwayTeamId ||
            string.IsNullOrWhiteSpace(input.HomeTeamName) || string.IsNullOrWhiteSpace(input.AwayTeamName) ||
            !double.IsFinite(input.PitchLengthMetres) || !double.IsFinite(input.PitchWidthMetres) ||
            input.PitchLengthMetres <= 0 || input.PitchWidthMetres <= 0 || input.Events is null || input.Frames is null)
            throw new ArgumentException("Tactical sequence input metadata is invalid.", nameof(input));
        if (input.SelectedState is null || input.SelectedState.MatchId != input.MatchId ||
            input.SelectedState.ClockSeconds != input.SelectedTimeSeconds || input.SelectedState.Provenance != input.Provenance ||
            input.SelectedState.HomeScore < 0 || input.SelectedState.AwayScore < 0 ||
            input.SelectedState.HomeActivePlayers is < 0 or > 11 || input.SelectedState.AwayActivePlayers is < 0 or > 11)
            throw new ArgumentException("Selected match context must match the selected time and source metadata.", nameof(input));
        if (policy.Publishable || policy.ReviewStatus != "unapproved-demo")
            throw new ArgumentException("This detector only accepts the unapproved demonstration policy.", nameof(policy));

        var eventIds = new HashSet<Guid>();
        var sequences = new HashSet<long>();
        long previousSequence = -1;
        var previousClock = -1;
        foreach (var item in input.Events)
        {
            if (item.Event.MatchId.ToString() != input.MatchId || item.Event.Sequence <= previousSequence ||
                item.Event.ClockSeconds < previousClock || item.Event.ClockSeconds > input.SelectedTimeSeconds ||
                !eventIds.Add(item.Event.EventId) || !sequences.Add(item.Event.Sequence))
                throw new InvalidDataException("Tactical input events must preserve unique ordered source identity and stay within selected time.");
            previousSequence = item.Event.Sequence;
            previousClock = item.Event.ClockSeconds;
        }
        if (input.Events.Count > 0 && input.Events[^1].Event.Sequence != input.ThroughSequence)
            throw new InvalidDataException("Tactical input through-sequence does not match the visible source prefix.");
        if (input.Events.Count == 0 && input.ThroughSequence != -1)
            throw new InvalidDataException("An empty tactical event prefix must use through-sequence -1.");

        var frameIds = new HashSet<string>(StringComparer.Ordinal);
        var frameClocks = new HashSet<int>();
        foreach (var frame in input.Frames)
        {
            if (string.IsNullOrWhiteSpace(frame.Id) || frame.ClockSeconds < 0 ||
                frame.ClockSeconds > input.SelectedTimeSeconds || !frameIds.Add(frame.Id) || !frameClocks.Add(frame.ClockSeconds) ||
                !double.IsFinite(frame.BallX) || !double.IsFinite(frame.BallY) || frame.BallX < 0 ||
                frame.BallX > input.PitchLengthMetres || frame.BallY < 0 || frame.BallY > input.PitchWidthMetres ||
                frame.Players.Any(player => !double.IsFinite(player.X) || !double.IsFinite(player.Y) ||
                    player.X < 0 || player.X > input.PitchLengthMetres || player.Y < 0 || player.Y > input.PitchWidthMetres ||
                    (player.TeamId != input.HomeTeamId && player.TeamId != input.AwayTeamId)))
                throw new InvalidDataException("Tactical input frames must be unique, finite, in-pitch and selected-time bounded.");
        }
    }
}
