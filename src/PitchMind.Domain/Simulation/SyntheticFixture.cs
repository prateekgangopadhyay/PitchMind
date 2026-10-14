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
    SyntheticTeam Home, SyntheticTeam Away, IReadOnlyList<SimulationEvent> Events, IReadOnlyList<TrackingFrame> Frames,
    string? CompetitionId = null, string? CompetitionRulesVersion = null, int? MaximumSubstituteReentriesPerPlayer = null)
{
    public string CoordinateConvention =>
        $"Offline synthetic fixture; metre coordinates, x=0..{PitchLengthMetres:R}, y=0..{PitchWidthMetres:R}; fixed stadium axes; attacking direction changes at {HalfTimeSeconds} seconds.";
}
public sealed record MatchSnapshot(string MatchId, int ClockSeconds, int Period, string Status, int HomeScore, int AwayScore,
    string Provenance, TrackingFrame Frame, long LastSequence, MatchPhase Phase, string? PossessionTeamId,
    int HomeActivePlayers, int AwayActivePlayers, IReadOnlyList<MatchPlayerState> Players, string CoordinateConvention,
    string? CompetitionId = null, string? CompetitionRulesVersion = null, int? MaximumSubstituteReentriesPerPlayer = null);
public sealed record TacticalInsight(string Id, string MatchId, int ClockSeconds, string Title, string Summary,
    string Classification, string DetectorVersion, IReadOnlyList<Guid> EventIds, IReadOnlyList<string> FrameIds);
public sealed record TacticalEvidence(TacticalInsight Insight, IReadOnlyList<SimulationEvent> Events, IReadOnlyList<TrackingFrame> Frames,
    string CoordinateConvention, IReadOnlyList<string> Conditions, IReadOnlyList<string> Limitations);
public sealed record MatchExplanation(string InsightId, int ClockSeconds, string Audience, string Source, string Text,
    IReadOnlyList<Guid> EventIds, IReadOnlyList<string> Limitations);
