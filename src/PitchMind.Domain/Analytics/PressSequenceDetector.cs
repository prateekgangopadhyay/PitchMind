using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    long? CanonicalRevision,
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
    IReadOnlyList<TrackingFrame> Frames,
    string SourceSha256)
{
    public MatchState? SelectedState { get; init; }
}

/// <summary>
/// Detects one specific synthetic press sequence. The unapproved demo policy and its result
/// cannot be represented as a publishable or canonical football fact.
/// </summary>
public static class PressSequenceDetector
{
    public static IReadOnlyList<TacticalInsight> Detect(SyntheticFixture fixture, int selectedTimeSeconds,
        MatchState? selectedState = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var events = fixture.Events.Where(item => item.Event.ClockSeconds <= selectedTimeSeconds).ToArray();
        var sourceJson = JsonSerializer.Serialize(fixture, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceJson)));
        var input = new PressSequenceInput(fixture.MatchId, fixture.Version, fixture.Provenance, null,
            selectedTimeSeconds, events.Length == 0 ? -1 : events[^1].Event.Sequence,
            fixture.Home.Id, fixture.Home.Name, fixture.Away.Id, fixture.Away.Name,
            "stadium-metres", fixture.PitchLengthMetres, fixture.PitchWidthMetres,
            Array.AsReadOnly(events), Array.AsReadOnly(fixture.Frames.Where(frame => frame.ClockSeconds <= selectedTimeSeconds).ToArray()),
            sourceSha256)
        {
            SelectedState = selectedState
        };
        return Detect(input);
    }

    public static IReadOnlyList<TacticalInsight> Detect(PressSequenceInput input,
        PressSequencePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        policy ??= PressSequencePolicy.UnapprovedDemoV2;
        Validate(input, policy);

        // Spatial distances are metres only when the fixture declares stadium metres.
        if (input.CoordinateSystem is not ("stadium-metres" or "metres-v1") || input.Events.Count == 0 || input.Frames.Count == 0)
            return Array.Empty<TacticalInsight>();

        var framesByClock = input.Frames.GroupBy(frame => frame.ClockSeconds)
            .ToDictionary(group => group.Key, group => group.ToArray());
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
            if (requiredFrameClocks.Any(clock => !framesByClock.TryGetValue(clock, out var frames) || frames.Length == 0)) continue;
            var pressureFrames = framesByClock[pressure.Event.ClockSeconds];
            if (pressureFrames.Length != 1) continue; // Ambiguous same-time tracking cannot support a distance claim.
            var pressureFrame = pressureFrames[0];
            var presser = pressureFrame.Players.SingleOrDefault(player => player.PlayerId == pressure.PlayerId &&
                player.TeamId == pressure.TeamId);
            var carrier = pressureFrame.Players.SingleOrDefault(player => player.PlayerId == pressure.RelatedPlayerId &&
                player.TeamId == trigger.TeamId);
            if (presser is null || carrier is null) continue;

            var distance = Math.Sqrt(Math.Pow(presser.X - carrier.X, 2) + Math.Pow(presser.Y - carrier.Y, 2));
            if (!double.IsFinite(distance) || distance > policy.MaximumPressureDistanceMetres) continue;

            var frameIds = requiredFrameClocks.SelectMany(clock => framesByClock[clock])
                .Select(frame => frame.Id).ToArray();
            var pressingTeam = pressure.TeamId == input.HomeTeamId ? input.HomeTeamName : input.AwayTeamName;
            var context = BuildContext(input, pressure.TeamId);
            var summary = context is null
                ? $"{pressingTeam}'s observed pressure is followed by a regain, cutback, shot and goal in this synthetic fixture; the sequence does not establish causality or tactical intent."
                : $"{pressingTeam}'s observed pressure is followed by a regain, cutback, shot and goal in this synthetic fixture. At the selected time the scoreline is {context.Scoreline}; this team's active-player difference is {context.NumericalAdvantage:+#;-#;0}. The sequence does not establish causality, intent, control, or comparative tactical quality.";
            var conditions = new List<string>
            {
                $"trigger: completed back-pass event {trigger.Event.EventId:D} (sequence {trigger.Event.Sequence})",
                $"pressure: opposing pressure event {pressure.Event.EventId:D}; observed separation {distance:F2}m <= {policy.MaximumPressureDistanceMetres:F0}m",
                $"response: incomplete opponent pass {response.Event.EventId:D} within {response.Event.ClockSeconds - pressure.Event.ClockSeconds}s",
                $"regain: same pressing team turnover {regain.Event.EventId:D} within {regain.Event.ClockSeconds - response.Event.ClockSeconds}s",
                $"exploitation: completed cutback {cutback.Event.EventId:D} followed by shot {shot.Event.EventId:D}",
                $"outcome: goal event {goal.Event.EventId:D} at {goal.Event.ClockSeconds}s; sequence ends {goal.Event.ClockSeconds - trigger.Event.ClockSeconds}s after trigger",
                $"coverage: {requiredFrameClocks.Length} distinct event-time frames include the pressure participants",
                $"selected-time: all evidence is at or before {input.SelectedTimeSeconds}s; through sequence {input.ThroughSequence}"
            };
            if (context is not null)
                conditions.Add($"context: {context.Scoreline}; pressing team role {context.TeamRole}; active-player difference {context.NumericalAdvantage}; match phase {context.MatchPhase}; opponent possession changes from in-possession at the trigger to defending after the observed regain");
            conditions.Add($"policy: {policy.Version} ({policy.ReviewStatus}); publication approval is absent");
            string EventNode(SimulationEvent item) => $"event:{item.Event.EventId:D}";
            string FrameNode(TrackingFrame frame) => $"frame:{frame.Id}";
            var evidenceConditions = Array.AsReadOnly(new[]
            {
                new TacticalEvidenceCondition("trigger", true,
                    $"Completed back-pass at {trigger.Event.ClockSeconds}s, sequence {trigger.Event.Sequence}.", null,
                    new[] { EventNode(trigger) }, null),
                new TacticalEvidenceCondition("pressure", true,
                    $"Observed separation {distance:F2}m at {pressure.Event.ClockSeconds}s.",
                    $"<= {policy.MaximumPressureDistanceMetres:F0}m (unapproved demonstration threshold)",
                    new[] { EventNode(pressure), FrameNode(pressureFrame) }, null),
                new TacticalEvidenceCondition("response", true,
                    $"Incomplete opponent pass {response.Event.ClockSeconds - pressure.Event.ClockSeconds}s after pressure.",
                    $"<= {policy.MaximumPressToResponseSeconds}s (unapproved demonstration threshold)",
                    new[] { EventNode(pressure), EventNode(response) }, null),
                new TacticalEvidenceCondition("regain", true,
                    $"Same pressing team regained possession {regain.Event.ClockSeconds - response.Event.ClockSeconds}s after response.",
                    $"<= {policy.MaximumResponseToRegainSeconds}s (unapproved demonstration threshold)",
                    new[] { EventNode(response), EventNode(regain) }, null),
                new TacticalEvidenceCondition("exploitation", true,
                    $"Completed cutback followed by a shot; cutback-to-shot interval {shot.Event.ClockSeconds - cutback.Event.ClockSeconds}s.",
                    $"<= {policy.MaximumRegainToOutcomeSeconds}s per action (unapproved demonstration threshold)",
                    new[] { EventNode(regain), EventNode(cutback), EventNode(shot) }, null),
                new TacticalEvidenceCondition("outcome", true,
                    $"Goal at {goal.Event.ClockSeconds}s, {goal.Event.ClockSeconds - trigger.Event.ClockSeconds}s after trigger.",
                    $"<= {policy.MaximumTriggerToOutcomeSeconds}s from trigger (unapproved demonstration threshold)",
                    new[] { EventNode(trigger), EventNode(goal) }, null),
                new TacticalEvidenceCondition("tracking-coverage", true,
                    $"{requiredFrameClocks.Length} event-time frames include the required pressure participants.", null,
                    requiredFrameClocks.SelectMany(clock => framesByClock[clock].Select(FrameNode)).ToArray(), null),
                new TacticalEvidenceCondition("selected-time", true,
                    $"All linked evidence is at or before {input.SelectedTimeSeconds}s and through sequence {input.ThroughSequence}.", null,
                    events.Select(EventNode).Concat(frameIds.Select(id => $"frame:{id}")).ToArray(), null),
                new TacticalEvidenceCondition("publication-policy", false,
                    $"Policy {policy.Version} status is {policy.ReviewStatus}.", "approved canonical policy",
                    Array.Empty<string>(), "The demonstration policy has not received football/Product Owner approval.")
            });

            var insight = new TacticalInsight(
                $"press-{trigger.Event.EventId:D}", input.MatchId, goal.Event.ClockSeconds,
                "Synthetic pressure sequence candidate",
                summary,
                "simulated", policy.Version,
                Array.AsReadOnly(events.Select(item => item.Event.EventId).ToArray()),
                Array.AsReadOnly(frameIds))
            {
                SourceVersion = input.SourceVersion,
                SourceSha256 = input.SourceSha256,
                Provenance = input.Provenance,
                CanonicalRevision = input.CanonicalRevision,
                SelectedTimeSeconds = input.SelectedTimeSeconds,
                ThroughSequence = input.ThroughSequence,
                PolicyStatus = policy.ReviewStatus,
                Publishable = policy.Publishable,
                Conditions = Array.AsReadOnly(conditions.ToArray()),
                ClaimTruthClass = "pattern",
                Uncertainty = "The demonstration policy is uncalibrated; no confidence score is available.",
                EvidenceConditions = evidenceConditions,
                Context = context
            };
            var graph = TacticalEvidenceGraphBuilder.Build(insight, input.Events, input.Frames, input.SelectedState);
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
            string.IsNullOrWhiteSpace(input.SourceSha256) || !IsSha256(input.SourceSha256) ||
            input.Provenance != "synthetic" || input.SelectedTimeSeconds < 0 ||
            input.CanonicalRevision is <= 0 || string.IsNullOrWhiteSpace(input.HomeTeamId) ||
            string.IsNullOrWhiteSpace(input.AwayTeamId) || input.HomeTeamId == input.AwayTeamId ||
            string.IsNullOrWhiteSpace(input.HomeTeamName) || string.IsNullOrWhiteSpace(input.AwayTeamName) ||
            !double.IsFinite(input.PitchLengthMetres) || !double.IsFinite(input.PitchWidthMetres) ||
            input.PitchLengthMetres <= 0 || input.PitchWidthMetres <= 0 || input.Events is null || input.Frames is null)
            throw new ArgumentException("Tactical sequence input metadata is invalid.", nameof(input));
        if (policy.Publishable || policy.ReviewStatus != "unapproved-demo")
            throw new ArgumentException("This detector only accepts the unapproved demonstration policy.", nameof(policy));

        var eventIds = new HashSet<Guid>();
        var sequences = new HashSet<long>();
        long previousSequence = -1;
        var previousClock = -1;
        foreach (var item in input.Events)
        {
            if (item.Event.MatchId.ToString() != input.MatchId || item.Event.Sequence != previousSequence + 1 ||
                item.Event.ClockSeconds < previousClock || item.Event.ClockSeconds > input.SelectedTimeSeconds ||
                !eventIds.Add(item.Event.EventId) || !sequences.Add(item.Event.Sequence))
                throw new InvalidDataException("Tactical input events must preserve a contiguous source prefix, unique ordered identity and selected-time bounds.");
            previousSequence = item.Event.Sequence;
            previousClock = item.Event.ClockSeconds;
        }
        if (input.Events.Count > 0 && input.Events[^1].Event.Sequence != input.ThroughSequence)
            throw new InvalidDataException("Tactical input through-sequence does not match the visible source prefix.");
        if (input.Events.Count == 0 && input.ThroughSequence != -1)
            throw new InvalidDataException("An empty tactical event prefix must use through-sequence -1.");

        if (input.SelectedState is { } selectedState &&
            (selectedState.MatchId != input.MatchId || selectedState.ClockSeconds != input.SelectedTimeSeconds ||
             selectedState.Provenance != input.Provenance || selectedState.HomeScore < 0 || selectedState.AwayScore < 0 ||
             selectedState.HomeActivePlayers is < 0 or > 11 || selectedState.AwayActivePlayers is < 0 or > 11))
            throw new ArgumentException("Selected match context must match the selected time and source metadata.", nameof(input));

        var frameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in input.Frames)
        {
            if (string.IsNullOrWhiteSpace(frame.Id) || frame.ClockSeconds < 0 ||
                frame.ClockSeconds > input.SelectedTimeSeconds || !frameIds.Add(frame.Id) ||
                !double.IsFinite(frame.BallX) || !double.IsFinite(frame.BallY) || frame.BallX < 0 ||
                frame.BallX > input.PitchLengthMetres || frame.BallY < 0 || frame.BallY > input.PitchWidthMetres ||
                frame.Players.Select(player => player.PlayerId).Distinct(StringComparer.Ordinal).Count() != frame.Players.Count ||
                frame.Players.Any(player => !double.IsFinite(player.X) || !double.IsFinite(player.Y) ||
                    player.X < 0 || player.X > input.PitchLengthMetres || player.Y < 0 || player.Y > input.PitchWidthMetres ||
                    (player.TeamId != input.HomeTeamId && player.TeamId != input.AwayTeamId)))
                throw new InvalidDataException("Tactical input frames must be unique, finite, in-pitch and selected-time bounded.");
        }
    }

    private static TacticalContext? BuildContext(PressSequenceInput input, string pressingTeamId)
    {
        if (input.SelectedState is not { } state) return null;
        var isHome = pressingTeamId == input.HomeTeamId;
        var activeDifference = isHome
            ? state.HomeActivePlayers - state.AwayActivePlayers
            : state.AwayActivePlayers - state.HomeActivePlayers;
        var scoreline = $"{input.HomeTeamName} {state.HomeScore}–{state.AwayScore} {input.AwayTeamName}";
        return new(TacticalContext.CurrentSchemaVersion, scoreline, isHome ? "home" : "away", activeDifference,
            "opponent in possession at trigger; defending after observed regain", state.Phase.ToString());
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
