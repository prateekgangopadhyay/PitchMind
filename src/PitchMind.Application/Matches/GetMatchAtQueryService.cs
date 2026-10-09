using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitchMind.Application.Matches;

public sealed record GetMatchAtQuery(MatchId MatchId, int SelectedSeconds, long UpToCanonicalRevision,
    string AlgorithmVersion, string DetectorVersion);

public sealed record MatchAtResult(int SelectedSeconds, SelectedMatchProjection Projection,
    IReadOnlyList<MatchEvent> Events, MatchState ReplayedState, string ReplaySha256,
    IReadOnlyList<TacticalInsight> DerivedInsights, string DetectorVersion,
    string ReplayInputSha256, string DerivedOutputSha256);

/// <summary>Composes a revision-pinned projection and its verified selected-time event window.</summary>
public sealed class GetMatchAtQueryService(IMatchProjectionReader reader)
{
    public async Task<MatchAtResult?> ExecuteAsync(GetMatchAtQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MatchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(query));
        if (query.UpToCanonicalRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Canonical revision must be positive.");
        if (string.IsNullOrWhiteSpace(query.AlgorithmVersion))
            throw new ArgumentException("Algorithm version is required.", nameof(query));
        if (string.IsNullOrWhiteSpace(query.DetectorVersion))
            throw new ArgumentException("Detector version is required.", nameof(query));
        if (!string.Equals(query.DetectorVersion, PressSequencePolicy.UnapprovedDemoV2.Version, StringComparison.Ordinal))
            throw new NotSupportedException($"Detector version '{query.DetectorVersion}' is not available for deterministic replay.");

        var snapshot = await reader.ReadReplaySnapshotAtAsync(query.MatchId, query.SelectedSeconds,
            query.UpToCanonicalRevision, query.AlgorithmVersion, cancellationToken);
        if (snapshot is null) return null;
        var projection = snapshot.Projection;
        if (projection.CanonicalRevision > query.UpToCanonicalRevision ||
            projection.AsOfClockSeconds > query.SelectedSeconds ||
            projection.AlgorithmVersion != query.AlgorithmVersion || projection.AsOfSequence < -1)
            throw new InvalidDataException("Selected projection metadata exceeds the requested match time or revision.");

        var events = new List<MatchEvent>();
        var previousClock = 0;
        foreach (var matchEvent in snapshot.Events)
        {
            if (matchEvent.MatchId != query.MatchId || matchEvent.Sequence != events.Count ||
                matchEvent.ClockSeconds > query.SelectedSeconds || matchEvent.ClockSeconds < previousClock)
                throw new InvalidDataException("Selected-time event window is inconsistent with its match, clock, or sequence.");
            previousClock = matchEvent.ClockSeconds;
            events.Add(matchEvent);
        }

        if (events.Count != projection.AsOfSequence + 1)
            throw new InvalidDataException("Selected-time event window does not reach the selected projection sequence.");

        var seed = snapshot.Seed
            ?? throw new InvalidDataException("Match has no immutable replay seed; canonical state cannot be replayed.");
        var aggregate = MatchAggregate.FromReplaySeed(query.MatchId, seed);
        var replayEvents = new List<SimulationEvent>();
        foreach (var item in snapshot.ReplayEvents)
        {
            if (item.Event.MatchId != query.MatchId || item.Event.Sequence != replayEvents.Count ||
                item.Event.ClockSeconds > query.SelectedSeconds)
                throw new InvalidDataException("Replay source event is outside the selected match-time prefix.");
            aggregate.Apply(item);
            replayEvents.Add(item);
        }
        if (replayEvents.Count != projection.AsOfSequence + 1)
            throw new InvalidDataException("Replay source prefix does not reach the selected projection sequence.");

        var context = snapshot.Context
            ?? throw new InvalidDataException("Match has no immutable replay context; derived outputs cannot be replayed.");
        ValidateReplayContext(context, seed, snapshot.SourceIdentity, projection);
        var playerTeams = seed.Players.ToDictionary(player => player.PlayerId, player => player.TeamId, StringComparer.Ordinal);
        var replayFrames = new List<MatchReplayFrame>();
        foreach (var item in snapshot.Frames)
        {
            var atFrame = MatchAggregate.FromReplaySeed(query.MatchId, seed);
            foreach (var sourceEvent in replayEvents.Where(sourceEvent => sourceEvent.Event.ClockSeconds <= item.Frame.ClockSeconds))
                atFrame.Apply(sourceEvent);
            var activeAtFrame = atFrame.Snapshot().Players.Where(player => player.Active)
                .Select(player => player.PlayerId).ToHashSet(StringComparer.Ordinal);
            ValidateReplayFrame(item, context, playerTeams, activeAtFrame, projection, query.SelectedSeconds,
                replayFrames.Count == 0 ? null : replayFrames[^1]);
            replayFrames.Add(item);
        }
        if (replayFrames.Count == 0)
            throw new InvalidDataException("Match has no tracking frames in the selected-time prefix; derived outputs cannot be replayed.");

        var replayedState = aggregate.Snapshot();
        var json = JsonSerializer.Serialize(replayedState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var replaySha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (replayedState.MatchId != projection.State.MatchId || replayedState.LastSequence != projection.AsOfSequence ||
            replayedState.ClockSeconds != projection.AsOfClockSeconds || replaySha256 != projection.StateSha256)
            throw new InvalidDataException("Deterministic replay state does not match the selected persisted projection digest.");

        var derivedInput = new PressSequenceInput(
            query.MatchId.ToString(), context.FixtureVersion, context.Provenance, projection.CanonicalRevision,
            query.SelectedSeconds, projection.AsOfSequence, seed.HomeTeamId, context.HomeTeamName,
            seed.AwayTeamId, context.AwayTeamName, context.CoordinateVersion,
            context.PitchLengthMetres, context.PitchWidthMetres,
            Array.AsReadOnly(replayEvents.ToArray()),
            Array.AsReadOnly(replayFrames.Select(frame => frame.Frame).ToArray()), projection.SourceSha256)
        {
            SelectedState = replayedState with { ClockSeconds = query.SelectedSeconds }
        };
        var derived = PressSequenceDetector.Detect(derivedInput);
        var replayInputJson = JsonSerializer.Serialize(new ReplayInput(query.SelectedSeconds, projection.CanonicalRevision,
            projection.AsOfSequence, query.AlgorithmVersion, query.DetectorVersion, context, seed, replayEvents, replayFrames), JsonOptions);
        var replayInputSha256 = Hash(replayInputJson);
        var derivedJson = JsonSerializer.Serialize(new DerivedReplayOutput(query.SelectedSeconds, projection.CanonicalRevision,
            projection.AsOfSequence, query.DetectorVersion, derived), JsonOptions);
        var derivedOutputSha256 = Hash(derivedJson);

        return new(query.SelectedSeconds, projection, Array.AsReadOnly(events.ToArray()), replayedState, replaySha256,
            derived, query.DetectorVersion, replayInputSha256, derivedOutputSha256);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ValidateReplayContext(MatchReplayContext context, MatchReplaySeed seed,
        MatchReplaySourceIdentity sourceIdentity, SelectedMatchProjection projection)
    {
        if (context.SchemaVersion != "match-replay-context-v1" || string.IsNullOrWhiteSpace(context.FixtureVersion) ||
            string.IsNullOrWhiteSpace(context.HomeTeamName) || string.IsNullOrWhiteSpace(context.AwayTeamName) ||
            context.Provenance != seed.Provenance || context.PitchLengthMetres is < 90 or > 120 ||
            context.PitchWidthMetres is < 45 or > 90 || context.DurationSeconds is < 1 or > 5400 ||
            context.HalfTimeSeconds <= 0 || context.HalfTimeSeconds >= context.DurationSeconds ||
            context.CoordinateVersion != "metres-v1" ||
            !context.CoordinateConvention.Contains("metre", StringComparison.OrdinalIgnoreCase) ||
            !context.CoordinateConvention.Contains("x=", StringComparison.OrdinalIgnoreCase) ||
            !context.CoordinateConvention.Contains("y=", StringComparison.OrdinalIgnoreCase) ||
            !context.CoordinateConvention.Contains("attacking direction", StringComparison.OrdinalIgnoreCase) ||
            !context.CoordinateConvention.Contains($"changes at {context.HalfTimeSeconds}", StringComparison.OrdinalIgnoreCase) ||
            !double.IsFinite(context.PitchLengthMetres) || !double.IsFinite(context.PitchWidthMetres) ||
            context.FixtureVersion != sourceIdentity.FixtureVersion || context.Provenance != sourceIdentity.Provenance ||
            context.PitchLengthMetres != sourceIdentity.PitchLengthMetres ||
            context.PitchWidthMetres != sourceIdentity.PitchWidthMetres ||
            seed.Provenance != sourceIdentity.Provenance || projection.State.Provenance != sourceIdentity.Provenance ||
            seed.SchemaVersion == "match-replay-seed-v2" && seed.CompetitionRules != sourceIdentity.CompetitionRules ||
            projection.State.CompetitionId != sourceIdentity.CompetitionRules.CompetitionId ||
            projection.State.CompetitionRulesVersion != sourceIdentity.CompetitionRules.Version ||
            projection.State.MaximumSubstituteReentriesPerPlayer != sourceIdentity.CompetitionRules.MaximumSubstituteReentriesPerPlayer)
            throw new InvalidDataException("Immutable replay context is incomplete, unsupported, or inconsistent with the match source.");
    }

    private static void ValidateReplayFrame(MatchReplayFrame item, MatchReplayContext context,
        IReadOnlyDictionary<string, string> playerTeams, IReadOnlySet<string> activeAtFrame,
        SelectedMatchProjection projection, int selectedSeconds, MatchReplayFrame? previous)
    {
        var frame = item.Frame;
        if (frame is null || string.IsNullOrWhiteSpace(frame.Id) || item.Sequence < 0 || item.Period is < 1 or > 2 ||
            item.CoordinateVersion != context.CoordinateVersion || item.SourceSha256 != projection.SourceSha256 ||
            frame.ClockSeconds < 0 || frame.ClockSeconds > selectedSeconds || frame.ClockSeconds > context.DurationSeconds ||
            frame.ClockSeconds < (previous?.Frame.ClockSeconds ?? 0) ||
            (previous is not null && item.Sequence <= previous.Sequence) ||
            frame.ClockSeconds < context.HalfTimeSeconds && item.Period != 1 ||
            frame.ClockSeconds >= context.HalfTimeSeconds && item.Period != 2 ||
            !double.IsFinite(frame.BallX) || frame.BallX < 0 || frame.BallX > context.PitchLengthMetres ||
            !double.IsFinite(frame.BallY) || frame.BallY < 0 || frame.BallY > context.PitchWidthMetres ||
            frame.Players is null || frame.Players.Select(player => player.PlayerId).Distinct(StringComparer.Ordinal).Count() != frame.Players.Count ||
            frame.Players.Any(player => string.IsNullOrWhiteSpace(player.PlayerId) || string.IsNullOrWhiteSpace(player.TeamId) ||
                !playerTeams.TryGetValue(player.PlayerId, out var teamId) || teamId != player.TeamId || !activeAtFrame.Contains(player.PlayerId) ||
                !double.IsFinite(player.X) || player.X < 0 || player.X > context.PitchLengthMetres ||
                !double.IsFinite(player.Y) || player.Y < 0 || player.Y > context.PitchWidthMetres))
            throw new InvalidDataException("Tracking replay input is invalid, unversioned, out of order, or later than selected time.");
    }

    private sealed record ReplayInput(int SelectedSeconds, long CanonicalRevision, long AsOfSequence,
        string AggregateAlgorithmVersion, string DetectorVersion, MatchReplayContext Context, MatchReplaySeed Seed,
        IReadOnlyList<SimulationEvent> Events, IReadOnlyList<MatchReplayFrame> Frames);

    private sealed record DerivedReplayOutput(int SelectedSeconds, long CanonicalRevision, long AsOfSequence,
        string DetectorVersion, IReadOnlyList<TacticalInsight> Insights);
}
