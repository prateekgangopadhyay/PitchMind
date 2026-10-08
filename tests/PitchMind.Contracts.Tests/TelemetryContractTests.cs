using System.Text.Json;
using PitchMind.Contracts.Telemetry.V1;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class TelemetryContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Event_contract_round_trips_original_identity_sequence_and_source_clock()
    {
        var original = Event("Pass") with
        {
            EventId = "canonical-event-01",
            Sequence = 0,
            MatchTime = new MatchTimeV1
            {
                Period = 2,
                ElapsedSeconds = 5761,
                SourceClockValue = 5761,
                SourceClockUnit = "seconds",
                SourceClockBasis = "match_elapsed"
            },
            SourceEventType = "ProgressivePass",
            Source = Source() with { SourceRecordId = "original-event-01", SourceSequence = 1 }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<EventEnvelopeV1>(json, JsonOptions)!;

        Assert.Equal("canonical-event-01", restored.EventId);
        Assert.Equal(0, restored.Sequence);
        Assert.Equal("original-event-01", restored.Source.SourceRecordId);
        Assert.Equal(1, restored.Source.SourceSequence);
        Assert.Equal("ProgressivePass", restored.SourceEventType);
        Assert.Equal(5761, restored.MatchTime.ElapsedSeconds);
        Assert.Equal(5761, restored.MatchTime.SourceClockValue);
        Assert.True(TelemetryContractValidatorV1.Validate(restored).IsValid);
    }

    [Theory]
    [InlineData(6, "Pass", "INVALID_PERIOD")]
    [InlineData(1, "NotAnEvent", "UNKNOWN_EVENT_TYPE")]
    [InlineData(1, "Pass", "UNSUPPORTED_SCHEMA_VERSION")]
    public void Event_contract_rejects_invalid_period_type_or_schema(int period, string eventType, string expectedError)
    {
        var value = Event(eventType) with
        {
            MatchTime = Event("Pass").MatchTime with { Period = period },
            SchemaVersion = expectedError == "UNSUPPORTED_SCHEMA_VERSION" ? 2 : 1
        };

        Assert.Contains(TelemetryContractValidatorV1.Validate(value).Errors, error => error.Code == expectedError);
    }

    [Fact]
    public void Event_contract_requires_player_for_player_action_and_returns_a_quarantine_reason()
    {
        var value = Event("Pass") with { PlayerId = null };

        var error = Assert.Single(TelemetryContractValidatorV1.Validate(value).Errors);
        Assert.Equal("REQUIRED_VALUE_MISSING", error.Code);
        Assert.Equal("playerId", error.Path);
    }

    [Fact]
    public void Event_contract_rejects_whitespace_values_non_utc_timestamp_and_incomplete_substitution()
    {
        var whitespaceErrors = TelemetryContractValidatorV1.Validate(Event("Pass") with
        {
            EventId = "   ",
            Source = Source() with { ProviderId = "\t" }
        }).Errors;
        Assert.Contains(whitespaceErrors, error => error.Path == "eventId");
        Assert.Contains(whitespaceErrors, error => error.Path == "source.providerId");

        var timestampErrors = TelemetryContractValidatorV1.Validate(Event("Pass") with
        {
            Source = Source() with { SourceTimestampUtc = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(5.5)) }
        }).Errors;
        Assert.Contains(timestampErrors, error => error.Code == "SOURCE_TIMESTAMP_NOT_UTC");

        var substitutionErrors = TelemetryContractValidatorV1.Validate(Event("Substitution") with { RelatedPlayerId = null }).Errors;
        Assert.Contains(substitutionErrors, error => error.Path == "relatedPlayerId");
    }

    [Fact]
    public void Versioned_json_schemas_are_valid_json_and_declare_runtime_string_and_substitution_rules()
    {
        var root = FindRepositoryRoot();
        using var eventSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "telemetry", "v1", "event-envelope.schema.json")));
        using var frameSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "telemetry", "v1", "tracking-frame.schema.json")));

        Assert.Equal(".*\\S.*", eventSchema.RootElement.GetProperty("properties").GetProperty("eventId").GetProperty("pattern").GetString());
        Assert.Equal("(?:Z|[+-]00:00)$", eventSchema.RootElement.GetProperty("$defs").GetProperty("source").GetProperty("properties").GetProperty("sourceTimestampUtc").GetProperty("pattern").GetString());
        Assert.Equal(".*\\S.*", frameSchema.RootElement.GetProperty("properties").GetProperty("matchId").GetProperty("pattern").GetString());
        Assert.Contains(eventSchema.RootElement.GetProperty("allOf").EnumerateArray(), rule =>
            rule.GetProperty("if").GetProperty("properties").GetProperty("eventType").GetProperty("const").GetString() == "Substitution" &&
            rule.GetProperty("then").GetProperty("required").EnumerateArray().Any(field => field.GetString() == "relatedPlayerId"));
    }

    [Fact]
    public void Tracking_frame_rejects_out_of_frame_coordinates_and_missing_team_direction()
    {
        var frame = Frame() with
        {
            Ball = new PitchPointV1 { X = 1.01m, Y = .5m },
            CoordinateSystem = Frame().CoordinateSystem with
            {
                TeamDirections = new[] { new TeamAttackingDirectionV1 { TeamId = "home", Direction = "right" } }
            }
        };

        var codes = TelemetryContractValidatorV1.Validate(frame).Errors.Select(error => error.Code).ToArray();
        Assert.Contains("INVALID_COORDINATE", codes);
        Assert.Contains("ATTACKING_DIRECTION_MISSING", codes);
    }

    [Fact]
    public void Declared_percent_and_axis_origin_normalize_to_stadium_metres()
    {
        var system = Frame().CoordinateSystem with { Unit = "percent_0_100", AxisOrigin = "top_left" };

        var normalized = TelemetryContractValidatorV1.NormalizeToStadiumMetres(
            new PitchPointV1 { X = 25, Y = 25 }, system);

        Assert.Equal(26.25m, normalized.XMetres);
        Assert.Equal(51m, normalized.YMetres);
    }

    [Fact]
    public void Team_relative_x_is_stable_across_halves_when_declared_directions_mirror()
    {
        var firstHalf = Frame().CoordinateSystem;
        var secondHalf = firstHalf with
        {
            TeamDirections = new[]
            {
                new TeamAttackingDirectionV1 { TeamId = "home", Direction = "left" },
                new TeamAttackingDirectionV1 { TeamId = "away", Direction = "right" }
            }
        };
        var attackingStadiumPointFirstHalf = TelemetryContractValidatorV1.NormalizeToStadiumMetres(
            new PitchPointV1 { X = .25m, Y = .5m }, firstHalf);
        var attackingStadiumPointSecondHalf = TelemetryContractValidatorV1.NormalizeToStadiumMetres(
            new PitchPointV1 { X = .75m, Y = .5m }, secondHalf);

        Assert.Equal(26.25m, TelemetryContractValidatorV1.ToAttackingXMetres(attackingStadiumPointFirstHalf, "home", firstHalf));
        Assert.Equal(26.25m, TelemetryContractValidatorV1.ToAttackingXMetres(attackingStadiumPointSecondHalf, "home", secondHalf));
        Assert.True(TelemetryContractValidatorV1.Validate(Frame()).IsValid);
        Assert.True(TelemetryContractValidatorV1.Validate(Frame() with { CoordinateSystem = secondHalf }).IsValid);
    }

    [Fact]
    public void Tracking_frame_rejects_duplicate_players_and_unknown_units()
    {
        var frame = Frame() with
        {
            Players = new[] { Frame().Players[0], Frame().Players[0] },
            CoordinateSystem = Frame().CoordinateSystem with { Unit = "unknown" }
        };

        var errors = TelemetryContractValidatorV1.Validate(frame).Errors;
        Assert.Contains(errors, error => error.Code == "DUPLICATE_PLAYER");
        Assert.Contains(errors, error => error.Code == "UNKNOWN_COORDINATE_UNIT");
    }

    [Fact]
    public void Tracking_frame_bounds_validation_work_for_oversized_player_arrays()
    {
        var frame = Frame() with
        {
            Players = Enumerable.Range(0, TelemetryContractValidatorV1.MaximumPlayersPerFrame + 1)
                .Select(index => new PlayerPositionV1
                {
                    PlayerId = $"player-{index}",
                    TeamId = index % 2 == 0 ? "home" : "away",
                    Position = new() { X = .5m, Y = .5m }
                }).ToArray()
        };

        Assert.Contains(TelemetryContractValidatorV1.Validate(frame).Errors, error => error.Code == "TOO_MANY_PLAYERS");
    }

    private static EventEnvelopeV1 Event(string type) => new()
    {
        SchemaVersion = 1,
        MatchId = "match-test",
        EventId = "event-test",
        Sequence = 0,
        MatchTime = new MatchTimeV1
        {
            Period = 1,
            ElapsedSeconds = 1,
            SourceClockValue = 1,
            SourceClockUnit = "seconds",
            SourceClockBasis = "match_elapsed"
        },
        EventType = type,
        SourceEventType = type,
        TeamId = type is "HalfTime" or "FullTime" ? null : "home",
        PlayerId = type is "HalfTime" or "FullTime" or "KickOff" ? null : "player-home-1",
        Outcome = "declared",
        Source = Source()
    };

    private static TrackingFrameV1 Frame() => new()
    {
        SchemaVersion = 1,
        MatchId = "match-test",
        FrameId = "frame-test",
        Sequence = 0,
        MatchTime = Event("Pass").MatchTime,
        CoordinateSystem = new PitchCoordinateSystemV1
        {
            LengthMetres = 105,
            WidthMetres = 68,
            Unit = "normalized_0_1",
            AxisOrigin = "bottom_left",
            TeamDirections = new[]
            {
                new TeamAttackingDirectionV1 { TeamId = "home", Direction = "right" },
                new TeamAttackingDirectionV1 { TeamId = "away", Direction = "left" }
            }
        },
        Ball = new PitchPointV1 { X = .5m, Y = .5m },
        Players = new[]
        {
            new PlayerPositionV1 { PlayerId = "player-home-1", TeamId = "home", Position = new() { X = .25m, Y = .5m } },
            new PlayerPositionV1 { PlayerId = "player-away-1", TeamId = "away", Position = new() { X = .75m, Y = .5m } }
        },
        Source = Source()
    };

    private static SourceReferenceV1 Source() => new()
    {
        ProviderId = "provider-test",
        SourceMatchId = "source-match-test",
        SourceRecordId = "source-record-test",
        SourceSequence = 0,
        SourceSchemaVersion = "source-v1",
        SourceSha256 = new string('a', 64),
        ProvenanceStatus = "owner_declared_synthetic",
        RightsReviewStatus = "pending"
    };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PitchMind.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not find PitchMind.slnx above the test output directory.");
    }
}
