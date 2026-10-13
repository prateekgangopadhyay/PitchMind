namespace PitchMind.Contracts.V1;

/// <summary>Stable public JSON contracts for the currently executable offline v1 match API.</summary>
public static class ContractVersion
{
    public const string V1 = "1";
}

public sealed record AnalystRequest(string InsightId, int AtSeconds, string Audience);

public sealed record PlayerResponse(string Id, string Name, string TeamId, int ShirtNumber, string Role);
public sealed record TeamResponse(string Id, string Name, string Formation, IReadOnlyList<PlayerResponse> Players);
public sealed record PitchPositionResponse(string PlayerId, string TeamId, double X, double Y);
public sealed record TrackingFrameResponse(string Id, int ClockSeconds, double BallX, double BallY, string CoordinateConvention, IReadOnlyList<PitchPositionResponse> Players);
public sealed record MatchEventResponse(Guid EventId, string MatchId, long Sequence, string Type, int ClockSeconds, int Period, DateTimeOffset OccurredAt);
public sealed record SimulationEventResponse(MatchEventResponse Event, string TeamId, string PlayerId, string Action, string Outcome, string? RelatedPlayerId);

public sealed record FixtureResponse(
    string MatchId,
    string Version,
    int Seed,
    string Provenance,
    int DurationSeconds,
    TeamResponse Home,
    TeamResponse Away,
    IReadOnlyList<SimulationEventResponse> Events,
    IReadOnlyList<TrackingFrameResponse> Frames);

public sealed record MatchStateResponse(
    string MatchId,
    int ClockSeconds,
    int Period,
    string Status,
    int HomeScore,
    int AwayScore,
    string Provenance,
    TrackingFrameResponse Frame,
    long LastSequence);

public sealed record InsightResponse(
    string Id,
    string MatchId,
    int ClockSeconds,
    string Title,
    string Summary,
    string Classification,
    string DetectorVersion,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<string> FrameIds);

public sealed record EvidenceResponse(
    InsightResponse Insight,
    IReadOnlyList<SimulationEventResponse> Events,
    IReadOnlyList<TrackingFrameResponse> Frames,
    string CoordinateConvention,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Limitations);

public sealed record ExplanationResponse(
    string InsightId,
    int ClockSeconds,
    string Audience,
    string Source,
    string Text,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<string> Limitations);

public sealed record ApiProblemResponse(
    string Type,
    string Title,
    int Status,
    string Code,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null);
