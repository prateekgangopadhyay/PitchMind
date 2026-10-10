namespace PitchMind.Contracts.Telemetry.V1;

/// <summary>Transport-neutral, versioned telemetry contracts. Provider values remain provenance, not canonical facts.</summary>
public sealed record EventEnvelopeV1
{
    public required int SchemaVersion { get; init; }
    public required string MatchId { get; init; }
    /// <summary>Stable canonical identifier; the original source identifier is retained in Source.</summary>
    public required string EventId { get; init; }
    /// <summary>Zero-based contiguous canonical order within the match.</summary>
    public required long Sequence { get; init; }
    public required MatchTimeV1 MatchTime { get; init; }
    public required string EventType { get; init; }
    /// <summary>Unmodified provider event label; it is not itself an approved canonical mapping.</summary>
    public required string SourceEventType { get; init; }
    public string? TeamId { get; init; }
    public string? PlayerId { get; init; }
    public string? RelatedPlayerId { get; init; }
    public string? Outcome { get; init; }
    /// <summary>Optional provider event location in its explicitly declared pitch coordinate system.</summary>
    public PitchCoordinateSystemV1? CoordinateSystem { get; init; }
    public EventLocationV1? Location { get; init; }
    public required SourceReferenceV1 Source { get; init; }
}

public sealed record EventLocationV1
{
    public PitchPointV1? Start { get; init; }
    public PitchPointV1? End { get; init; }
}

public sealed record TrackingFrameV1
{
    public required int SchemaVersion { get; init; }
    public required string MatchId { get; init; }
    public required string FrameId { get; init; }
    /// <summary>Zero-based canonical order. Source sequence remains unchanged under Source.</summary>
    public required long Sequence { get; init; }
    public required MatchTimeV1 MatchTime { get; init; }
    public required PitchCoordinateSystemV1 CoordinateSystem { get; init; }
    public required PitchPointV1 Ball { get; init; }
    public required IReadOnlyList<PlayerPositionV1> Players { get; init; }
    public required SourceReferenceV1 Source { get; init; }
}

public sealed record MatchTimeV1
{
    public required int Period { get; init; }
    /// <summary>Canonical elapsed seconds; no implicit 90-minute cap is applied.</summary>
    public required long ElapsedSeconds { get; init; }
    /// <summary>Unmodified value from the source clock.</summary>
    public required decimal SourceClockValue { get; init; }
    public required string SourceClockUnit { get; init; }
    public required string SourceClockBasis { get; init; }
}

public sealed record SourceReferenceV1
{
    public required string ProviderId { get; init; }
    public required string SourceMatchId { get; init; }
    /// <summary>Original provider event/frame identifier, preserved as text.</summary>
    public required string SourceRecordId { get; init; }
    /// <summary>Original provider sequence; its indexing is not rewritten.</summary>
    public required long SourceSequence { get; init; }
    public required string SourceSchemaVersion { get; init; }
    /// <summary>Lowercase SHA-256 of the immutable source artifact.</summary>
    public required string SourceSha256 { get; init; }
    /// <summary>Optional source wall-clock timestamp; distinct from match time.</summary>
    public DateTimeOffset? SourceTimestampUtc { get; init; }
    /// <summary>Owner declaration is distinct from independent rights/privacy review.</summary>
    public required string ProvenanceStatus { get; init; }
    public required string RightsReviewStatus { get; init; }
}

public sealed record PitchCoordinateSystemV1
{
    public required decimal LengthMetres { get; init; }
    public required decimal WidthMetres { get; init; }
    /// <summary>metres, normalized_0_1, or percent_0_100.</summary>
    public required string Unit { get; init; }
    /// <summary>bottom_left or top_left; conversion to stadium metres is explicit.</summary>
    public required string AxisOrigin { get; init; }
    public required IReadOnlyList<TeamAttackingDirectionV1> TeamDirections { get; init; }
}

public sealed record TeamAttackingDirectionV1
{
    public required string TeamId { get; init; }
    /// <summary>right means increasing stadium x; left means decreasing stadium x.</summary>
    public required string Direction { get; init; }
}

public sealed record PitchPointV1
{
    public required decimal X { get; init; }
    public required decimal Y { get; init; }
}

public sealed record PlayerPositionV1
{
    public required string PlayerId { get; init; }
    public required string TeamId { get; init; }
    public required PitchPointV1 Position { get; init; }
}

public sealed record TelemetryValidationError(string Code, string Path, string Message);

public sealed record TelemetryValidationResult(IReadOnlyList<TelemetryValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public static TelemetryValidationResult Valid { get; } = new(Array.Empty<TelemetryValidationError>());
}

public sealed record NormalizedPitchPoint(decimal XMetres, decimal YMetres);
