using System.Text;

namespace PitchMind.Contracts.Telemetry.V1;

/// <summary>Pure v1 validation and coordinate normalization; provider mapping stays outside this contract.</summary>
public static class TelemetryContractValidatorV1
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumPlayersPerFrame = 64;
    public const int MaximumIdentifierLength = 128;
    public const int MaximumTextLength = 256;
    public const decimal MinimumPitchLengthMetres = 90m;
    public const decimal MaximumPitchLengthMetres = 120m;
    public const decimal MinimumPitchWidthMetres = 45m;
    public const decimal MaximumPitchWidthMetres = 90m;

    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    {
        "KickOff", "Pass", "Shot", "Goal", "Foul", "Turnover", "FullTime", "Pressure", "HalfTime",
        "BallRecovery", "Carry", "Clearance", "Interception", "PressureApplied", "PossessionLost",
        "ProgressivePass", "ProgressiveCarry", "LineBreakingPass", "Tackle", "Substitution", "YellowCard", "RedCard"
    };

    private static readonly HashSet<string> PlayerEventTypes = new(StringComparer.Ordinal)
    {
        "Pass", "Shot", "Goal", "Foul", "Turnover", "Pressure", "PressureApplied", "BallRecovery", "Carry",
        "Clearance", "Interception", "PossessionLost", "ProgressivePass", "ProgressiveCarry", "LineBreakingPass",
        "Tackle", "Substitution", "YellowCard", "RedCard"
    };

    public static TelemetryValidationResult Validate(EventEnvelopeV1? value)
    {
        if (value is null) return Invalid("INVALID_ENVELOPE", "$", "Event envelope is required.");
        var errors = new List<TelemetryValidationError>();
        Version(value.SchemaVersion, errors);
        Required(value.MatchId, "matchId", errors);
        MaximumLength(value.MatchId, "matchId", MaximumIdentifierLength, errors);
        CanonicalRevision(value.CanonicalRevision, errors);
        Required(value.EventId, "eventId", errors);
        MaximumLength(value.EventId, "eventId", MaximumIdentifierLength, errors, "IDENTIFIER_TOO_LONG");
        if (value.Sequence < 0) Add(errors, "INVALID_SEQUENCE", "sequence", "Canonical sequence must be non-negative.");
        Time(value.MatchTime, errors);
        if (string.IsNullOrWhiteSpace(value.EventType) || !EventTypes.Contains(value.EventType))
            Add(errors, "UNKNOWN_EVENT_TYPE", "eventType", "Event type is not defined by telemetry v1.");
        Required(value.SourceEventType, "sourceEventType", errors);
        MaximumLength(value.SourceEventType, "sourceEventType", MaximumTextLength, errors);
        var playerEvent = value.EventType is not null && PlayerEventTypes.Contains(value.EventType);
        if (!playerEvent)
        {
            Optional(value.TeamId, "teamId", errors);
            MaximumLength(value.TeamId, "teamId", MaximumIdentifierLength, errors);
            Optional(value.PlayerId, "playerId", errors);
            MaximumLength(value.PlayerId, "playerId", MaximumIdentifierLength, errors);
        }
        if (value.EventType != "Substitution")
        {
            Optional(value.RelatedPlayerId, "relatedPlayerId", errors);
            MaximumLength(value.RelatedPlayerId, "relatedPlayerId", MaximumIdentifierLength, errors);
        }
        Optional(value.Outcome, "outcome", errors);
        MaximumLength(value.Outcome, "outcome", MaximumTextLength, errors);
        if (playerEvent)
        {
            Required(value.TeamId, "teamId", errors);
            MaximumLength(value.TeamId, "teamId", MaximumIdentifierLength, errors);
            Required(value.PlayerId, "playerId", errors);
            MaximumLength(value.PlayerId, "playerId", MaximumIdentifierLength, errors);
        }
        if (value.EventType == "Substitution")
        {
            Required(value.RelatedPlayerId, "relatedPlayerId", errors);
            MaximumLength(value.RelatedPlayerId, "relatedPlayerId", MaximumIdentifierLength, errors);
        }
        if (value.Location is not null)
        {
            if (value.CoordinateSystem is null)
            {
                Add(errors, "COORDINATE_SYSTEM_REQUIRED", "coordinateSystem", "Event coordinates require an explicit coordinate system.");
            }
            else
            {
                CoordinateSystem(value.CoordinateSystem, errors);
                TeamDirections(value.CoordinateSystem.TeamDirections, errors);
                if (value.Location.Start is null && value.Location.End is null)
                    Add(errors, "EVENT_LOCATION_REQUIRED", "location", "At least one event location point is required.");
                if (value.Location.Start is not null) Point(value.Location.Start, value.CoordinateSystem, "location.start", errors);
                if (value.Location.End is not null) Point(value.Location.End, value.CoordinateSystem, "location.end", errors);
            }
        }
        else if (value.CoordinateSystem is not null)
        {
            Add(errors, "EVENT_LOCATION_REQUIRED", "location", "A coordinate system is only valid when an event location is supplied.");
        }
        Source(value.Source, errors);
        return new(errors);
    }

    public static TelemetryValidationResult Validate(TrackingFrameV1? value)
    {
        if (value is null) return Invalid("INVALID_FRAME", "$", "Tracking frame is required.");
        var errors = new List<TelemetryValidationError>();
        Version(value.SchemaVersion, errors);
        Required(value.MatchId, "matchId", errors);
        MaximumLength(value.MatchId, "matchId", MaximumIdentifierLength, errors);
        CanonicalRevision(value.CanonicalRevision, errors);
        Required(value.FrameId, "frameId", errors);
        MaximumLength(value.FrameId, "frameId", MaximumIdentifierLength, errors);
        if (value.Sequence < 0) Add(errors, "INVALID_SEQUENCE", "sequence", "Canonical sequence must be non-negative.");
        Time(value.MatchTime, errors);
        Source(value.Source, errors);

        var system = value.CoordinateSystem;
        if (system is null)
        {
            Add(errors, "COORDINATE_SYSTEM_REQUIRED", "coordinateSystem", "Coordinate system must be declared.");
            return new(errors);
        }
        CoordinateSystem(system, errors);
        Point(value.Ball, system, "ball", errors);
        if (value.Players is null)
        {
            Add(errors, "PLAYERS_REQUIRED", "players", "Player list is required.");
            return new(errors);
        }
        if (value.Players.Count == 0) Add(errors, "PLAYERS_REQUIRED", "players", "At least one player position is required.");
        if (value.Players.Count > MaximumPlayersPerFrame) Add(errors, "TOO_MANY_PLAYERS", "players", $"A frame cannot exceed {MaximumPlayersPerFrame} player positions.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var teams = new HashSet<string>(StringComparer.Ordinal);
        // Cap work on an already-materialized invalid frame. The transport adapter must also cap bytes before deserialization.
        for (var index = 0; index < Math.Min(value.Players.Count, MaximumPlayersPerFrame); index++)
        {
            var player = value.Players[index];
            var path = $"players[{index}]";
            if (player is null)
            {
                Add(errors, "PLAYER_REQUIRED", path, "Player position is required.");
                continue;
            }
            Required(player.PlayerId, $"{path}.playerId", errors);
            MaximumLength(player.PlayerId, $"{path}.playerId", MaximumIdentifierLength, errors);
            Required(player.TeamId, $"{path}.teamId", errors);
            MaximumLength(player.TeamId, $"{path}.teamId", MaximumIdentifierLength, errors);
            if (!string.IsNullOrWhiteSpace(player.PlayerId) && !seen.Add(player.PlayerId))
                Add(errors, "DUPLICATE_PLAYER", $"{path}.playerId", "Player IDs must be unique within a frame.");
            if (!string.IsNullOrWhiteSpace(player.TeamId)) teams.Add(player.TeamId);
            Point(player.Position, system, $"{path}.position", errors);
        }
        TeamDirections(system.TeamDirections, errors, teams);
        return new(errors);
    }

    public static NormalizedPitchPoint NormalizeToStadiumMetres(PitchPointV1 point, PitchCoordinateSystemV1 system)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(system);
        var errors = new List<TelemetryValidationError>();
        CoordinateSystem(system, errors);
        Point(point, system, "point", errors);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.Select(error => $"{error.Path}: {error.Message}")), nameof(point));

        var (xScale, yScale) = system.Unit switch
        {
            "metres" => (1m, 1m),
            "normalized_0_1" => (system.LengthMetres, system.WidthMetres),
            "percent_0_100" => (system.LengthMetres / 100m, system.WidthMetres / 100m),
            _ => throw new ArgumentException("Unsupported coordinate unit.", nameof(system))
        };
        var yMetres = point.Y * yScale;
        if (system.AxisOrigin == "top_left") yMetres = system.WidthMetres - yMetres;
        return new(point.X * xScale, yMetres);
    }

    public static decimal ToAttackingXMetres(NormalizedPitchPoint point, string teamId, PitchCoordinateSystemV1 system)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(system);
        var direction = system.TeamDirections?.SingleOrDefault(item => string.Equals(item.TeamId, teamId, StringComparison.Ordinal))
            ?? throw new ArgumentException("Team attacking direction must be declared.", nameof(teamId));
        return direction.Direction switch
        {
            "right" => point.XMetres,
            "left" => system.LengthMetres - point.XMetres,
            _ => throw new ArgumentException("Attacking direction must be left or right.", nameof(system))
        };
    }

    private static void CoordinateSystem(PitchCoordinateSystemV1 system, List<TelemetryValidationError> errors)
    {
        if (system.LengthMetres is < MinimumPitchLengthMetres or > MaximumPitchLengthMetres)
            Add(errors, "INVALID_PITCH_DIMENSION", "coordinateSystem.lengthMetres", $"Pitch length must be between {MinimumPitchLengthMetres} and {MaximumPitchLengthMetres} metres.");
        if (system.WidthMetres is < MinimumPitchWidthMetres or > MaximumPitchWidthMetres)
            Add(errors, "INVALID_PITCH_DIMENSION", "coordinateSystem.widthMetres", $"Pitch width must be between {MinimumPitchWidthMetres} and {MaximumPitchWidthMetres} metres.");
        if (system.Unit is not ("metres" or "normalized_0_1" or "percent_0_100"))
            Add(errors, "UNKNOWN_COORDINATE_UNIT", "coordinateSystem.unit", "Coordinate unit must be explicit and supported.");
        if (system.AxisOrigin is not ("bottom_left" or "top_left"))
            Add(errors, "UNKNOWN_AXIS_ORIGIN", "coordinateSystem.axisOrigin", "Axis origin must be bottom_left or top_left.");
    }

    private static void TeamDirections(
        IReadOnlyList<TeamAttackingDirectionV1>? teamDirections,
        List<TelemetryValidationError> errors,
        IEnumerable<string>? requiredTeams = null)
    {
        if (teamDirections is null)
        {
            Add(errors, "TEAM_DIRECTIONS_REQUIRED", "coordinateSystem.teamDirections", "Team directions are required.");
            return;
        }
        if (teamDirections.Count == 0)
            Add(errors, "TEAM_DIRECTIONS_REQUIRED", "coordinateSystem.teamDirections", "At least one team direction is required.");
        if (teamDirections.Count > MaximumPlayersPerFrame)
            Add(errors, "TOO_MANY_TEAM_DIRECTIONS", "coordinateSystem.teamDirections", $"A coordinate system cannot exceed {MaximumPlayersPerFrame} team directions.");

        var declaredTeams = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Math.Min(teamDirections.Count, MaximumPlayersPerFrame); index++)
        {
            var direction = teamDirections[index];
            var path = $"coordinateSystem.teamDirections[{index}]";
            if (direction is null)
            {
                Add(errors, "TEAM_DIRECTION_REQUIRED", path, "Team direction is required.");
                continue;
            }
            Required(direction.TeamId, $"{path}.teamId", errors);
            MaximumLength(direction.TeamId, $"{path}.teamId", MaximumIdentifierLength, errors);
            if (direction.Direction is not ("left" or "right"))
                Add(errors, "UNKNOWN_ATTACK_DIRECTION", $"{path}.direction", "Attacking direction must be left or right.");
            if (!string.IsNullOrWhiteSpace(direction.TeamId) && !declaredTeams.Add(direction.TeamId))
                Add(errors, "DUPLICATE_TEAM_DIRECTION", $"{path}.teamId", "Team direction must be declared once.");
        }

        if (requiredTeams is null) return;
        foreach (var team in requiredTeams)
            if (!declaredTeams.Contains(team))
                Add(errors, "ATTACKING_DIRECTION_MISSING", "coordinateSystem.teamDirections", "No attacking direction is declared for a team referenced by the frame.");
    }

    private static void Point(PitchPointV1? point, PitchCoordinateSystemV1 system, string path, List<TelemetryValidationError> errors)
    {
        if (point is null) { Add(errors, "COORDINATE_REQUIRED", path, "Coordinate point is required."); return; }
        if (system.LengthMetres <= 0 || system.WidthMetres <= 0) return;
        var (maxX, maxY) = system.Unit switch
        {
            "metres" => (system.LengthMetres, system.WidthMetres),
            "normalized_0_1" => (1m, 1m),
            "percent_0_100" => (100m, 100m),
            _ => (decimal.MinValue, decimal.MinValue)
        };
        if (point.X < 0 || point.X > maxX) Add(errors, "INVALID_COORDINATE", $"{path}.x", "X coordinate is outside its declared frame.");
        if (point.Y < 0 || point.Y > maxY) Add(errors, "INVALID_COORDINATE", $"{path}.y", "Y coordinate is outside its declared frame.");
    }

    private static void Time(MatchTimeV1? time, List<TelemetryValidationError> errors)
    {
        if (time is null) { Add(errors, "MATCH_TIME_REQUIRED", "matchTime", "Match time is required."); return; }
        if (time.Period is < 1 or > 5) Add(errors, "INVALID_PERIOD", "matchTime.period", "Period must be between 1 and 5.");
        if (time.ElapsedSeconds < 0) Add(errors, "INVALID_MATCH_TIME", "matchTime.elapsedSeconds", "Elapsed seconds must be non-negative.");
        if (time.SourceClockValue < 0) Add(errors, "INVALID_SOURCE_CLOCK", "matchTime.sourceClockValue", "Source clock must be non-negative.");
        if (time.SourceClockUnit is not ("seconds" or "milliseconds" or "provider_defined"))
            Add(errors, "UNKNOWN_SOURCE_CLOCK_UNIT", "matchTime.sourceClockUnit", "Source clock unit must be explicit.");
        if (time.SourceClockBasis is not ("match_elapsed" or "period_elapsed" or "provider_defined"))
            Add(errors, "UNKNOWN_SOURCE_CLOCK_BASIS", "matchTime.sourceClockBasis", "Source clock basis must be explicit.");
    }

    private static void Source(SourceReferenceV1? source, List<TelemetryValidationError> errors)
    {
        if (source is null) { Add(errors, "SOURCE_PROVENANCE_REQUIRED", "source", "Source provenance is required."); return; }
        Required(source.ProviderId, "source.providerId", errors);
        MaximumLength(source.ProviderId, "source.providerId", MaximumIdentifierLength, errors);
        Required(source.SourceMatchId, "source.sourceMatchId", errors);
        MaximumLength(source.SourceMatchId, "source.sourceMatchId", MaximumIdentifierLength, errors);
        Required(source.SourceRecordId, "source.sourceRecordId", errors);
        MaximumLength(source.SourceRecordId, "source.sourceRecordId", MaximumIdentifierLength, errors);
        Required(source.SourceSchemaVersion, "source.sourceSchemaVersion", errors);
        MaximumLength(source.SourceSchemaVersion, "source.sourceSchemaVersion", MaximumTextLength, errors);
        Required(source.ProvenanceStatus, "source.provenanceStatus", errors);
        MaximumLength(source.ProvenanceStatus, "source.provenanceStatus", MaximumTextLength, errors);
        Required(source.RightsReviewStatus, "source.rightsReviewStatus", errors);
        MaximumLength(source.RightsReviewStatus, "source.rightsReviewStatus", MaximumTextLength, errors);
        if (source.SourceSequence < 0) Add(errors, "INVALID_SOURCE_SEQUENCE", "source.sourceSequence", "Original sequence must be non-negative.");
        if (source.SourceSha256 is null || source.SourceSha256.Length != 64 || source.SourceSha256.Any(c => !Uri.IsHexDigit(c)) || source.SourceSha256.Any(char.IsUpper))
            Add(errors, "INVALID_SOURCE_HASH", "source.sourceSha256", "Source hash must be lowercase SHA-256 hexadecimal.");
        if (source.SourceTimestampUtc is { Offset: var offset } && offset != TimeSpan.Zero)
            Add(errors, "SOURCE_TIMESTAMP_NOT_UTC", "source.sourceTimestampUtc", "Source timestamp must use UTC offset.");
    }

    private static void Version(int version, List<TelemetryValidationError> errors)
    {
        if (version != CurrentSchemaVersion) Add(errors, "UNSUPPORTED_SCHEMA_VERSION", "schemaVersion", $"Schema version {version} is unsupported; expected {CurrentSchemaVersion}.");
    }

    private static void CanonicalRevision(long revision, List<TelemetryValidationError> errors)
    {
        if (revision < 1)
            Add(errors, "INVALID_CANONICAL_REVISION", "canonicalRevision", "Canonical revision must be a positive store-assigned per-match revision.");
    }

    private static void Required(string? value, string path, List<TelemetryValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(errors, "REQUIRED_VALUE_MISSING", path, "A non-empty value is required.");
    }

    private static void Optional(string? value, string path, List<TelemetryValidationError> errors)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            Add(errors, "REQUIRED_VALUE_MISSING", path, "A supplied value must not be empty or whitespace.");
    }

    private static void MaximumLength(string? value, string path, int maximum, List<TelemetryValidationError> errors,
        string errorCode = "VALUE_TOO_LONG")
    {
        if (value is not null && value.EnumerateRunes().Skip(maximum).Any())
            Add(errors, errorCode, path, $"Value must not exceed {maximum} characters.");
    }

    private static TelemetryValidationResult Invalid(string code, string path, string message) => new(new[] { new TelemetryValidationError(code, path, message) });
    private static void Add(List<TelemetryValidationError> errors, string code, string path, string message) => errors.Add(new(code, path, message));
}
