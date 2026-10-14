using System.Text.Json.Serialization;

namespace PitchMind.Contracts.V1;

/// <summary>Stable public JSON contracts for the currently executable offline v1 match API.</summary>
public static class ContractVersion
{
    public const string V1 = "1";
}

public sealed record AnalystRequest(
    string InsightId,
    int AtSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Audience = null);

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
    int HalfTimeSeconds,
    double PitchLengthMetres,
    double PitchWidthMetres,
    TeamResponse Home,
    TeamResponse Away,
    IReadOnlyList<SimulationEventResponse> Events,
    IReadOnlyList<TrackingFrameResponse> Frames);

public sealed record MatchStateResponse(
    string MatchId,
    int ClockSeconds,
    int Period,
    string Status,
    string Phase,
    int HomeScore,
    int AwayScore,
    string Provenance,
    TrackingFrameResponse Frame,
    long LastSequence,
    string? PossessionTeamId,
    int HomeActivePlayers,
    int AwayActivePlayers,
    IReadOnlyList<MatchPlayerStateResponse> Players,
    string? CompetitionId = null,
    string? CompetitionRulesVersion = null,
    int? MaximumSubstituteReentriesPerPlayer = null);

public sealed record MatchPlayerStateResponse(string PlayerId, string TeamId, bool Active, bool Dismissed, int YellowCards);

public sealed record InsightResponse(
    string Id,
    string MatchId,
    int ClockSeconds,
    string Title,
    string Summary,
    string Classification,
    string DetectorVersion,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<string> FrameIds,
    string SourceVersion,
    string SourceSha256,
    string Provenance,
    long? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    string PolicyStatus,
    bool Publishable,
    IReadOnlyList<string> Conditions,
    TacticalContextResponse? Context,
    EvidenceGraphResponse? EvidenceGraph);

public sealed record TacticalContextResponse(
    string SchemaVersion,
    string Scoreline,
    string TeamRole,
    int NumericalAdvantage,
    string OpponentPhase,
    string MatchPhase);

public sealed record EvidenceGraphResponse(
    string MatchId,
    string SourceVersion,
    string SourceSha256,
    string Provenance,
    long? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    string DetectorVersion,
    string PolicyStatus,
    bool Publishable,
    string ClaimNodeId,
    IReadOnlyList<EvidenceNodeResponse> Nodes,
    IReadOnlyList<EvidenceEdgeResponse> Edges,
    string Uncertainty,
    IReadOnlyList<EvidenceConditionResponse> Conditions);

public sealed record EvidenceNodeResponse(
    string Id,
    string Kind,
    string TruthClass,
    string Description,
    Guid? EventId,
    string? FrameId,
    int? ClockSeconds,
    long? Sequence,
    string SourceVersion,
    string SourceSha256,
    long? CanonicalRevision,
    decimal? NumericValue,
    string? Unit);

public sealed record EvidenceEdgeResponse(string FromNodeId, string ToNodeId, string Relation);

public sealed record EvidenceConditionResponse(
    string Id,
    bool Passed,
    string Observed,
    string? Threshold,
    IReadOnlyList<string> SourceNodeIds,
    string? ExclusionReason);

public sealed record KeyMomentResponse(
    string Id, string MatchId, string SourceVersion, string SourceSha256, string Provenance, long? CanonicalRevision,
    int SelectedTimeSeconds, long ThroughSequence, int ClockSeconds, long Sequence,
    string Kind, string TruthClass, string Title, string Summary, decimal Relevance,
    string PolicyVersion, string PolicyStatus, bool Publishable,
    IReadOnlyList<Guid> EventIds, IReadOnlyList<string> FrameIds, IReadOnlyList<string> EvidenceClaimNodeIds);

public sealed record MechanismReplayResponse(
    string Id,
    string MatchId,
    string SourceVersion,
    string SourceSha256,
    string Provenance,
    long? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    int StartSeconds,
    int EndSeconds,
    string DetectorVersion,
    string PolicyStatus,
    bool Publishable,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<Guid> MechanismEventIds,
    IReadOnlyList<string> FrameIds,
    IReadOnlyList<string> ParticipantIds,
    IReadOnlyList<SimulationEventResponse> Events,
    IReadOnlyList<TrackingFrameResponse> Frames,
    string CoordinateConvention,
    EvidenceGraphResponse EvidenceGraph,
    IReadOnlyList<string> Limitations);

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

/// <summary>A version-pinned selected-time response from the offline synthetic replay path.</summary>
public sealed record ReplayResponse(
    string ExecutionMode,
    string MatchId,
    int SelectedElapsedSeconds,
    string FixtureVersion,
    int FixtureSeed,
    string ReplayAlgorithmVersion,
    string DetectorVersion,
    string ReplayInputSha256,
    string ReplayOutputSha256,
    long? CanonicalRevision,
    MatchStateResponse State,
    IReadOnlyList<SimulationEventResponse> Events,
    IReadOnlyList<InsightResponse> Insights);

public sealed record ApiProblemResponse(
    string Type,
    string Title,
    int Status,
    string Code,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null);

public sealed record CanonicalMatchCardResponse(
    string MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    int AsOfSeconds,
    long ThroughSequence,
    string Phase,
    int HomeScore,
    int AwayScore,
    string Provenance,
    string AlgorithmVersion,
    string SourceSha256);

public sealed record CanonicalMatchPageResponse(
    IReadOnlyList<CanonicalMatchCardResponse> Items,
    string? NextMatchId,
    bool HasMore);

public sealed record CanonicalMatchSnapshotResponse(
    string MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    int SelectedTimeSeconds,
    int AsOfSeconds,
    long ThroughSequence,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256,
    string Provenance,
    int Period,
    string Phase,
    int HomeScore,
    int AwayScore,
    string? PossessionTeamId,
    int HomeActivePlayers,
    int AwayActivePlayers,
    IReadOnlyList<MatchPlayerStateResponse> Players);

public sealed record CanonicalEventPageResponse(
    string MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    IReadOnlyList<MatchEventResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CanonicalEvidenceReferenceResponse(string Kind, string Id, string Relation);

public sealed record CanonicalInsightResponse(
    string InsightId,
    string MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    string SourceVersion,
    string Provenance,
    string SourceSha256,
    string DetectorVersion,
    string TruthClass,
    string Summary,
    IReadOnlyList<CanonicalEvidenceReferenceResponse> Evidence);

public sealed record CanonicalInsightPageResponse(
    string MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    IReadOnlyList<CanonicalInsightResponse> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CanonicalEvidencePayloadResponse(
    string Kind,
    string Id,
    string Relation,
    long SourceCanonicalRevision,
    long Sequence,
    int ClockSeconds,
    int Period,
    System.Text.Json.JsonElement Payload);

public sealed record CanonicalEvidenceResponse(
    CanonicalInsightResponse Insight,
    IReadOnlyList<CanonicalEvidencePayloadResponse> Items);

public sealed record CanonicalReplayResponse(
    CanonicalMatchSnapshotResponse Snapshot,
    CanonicalEventPageResponse Events);
