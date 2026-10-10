using System.Text.Json;
using System.Text.Json.Nodes;
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
            CanonicalRevision = 7,
            Sequence = 0,
            CoordinateSystem = Frame().CoordinateSystem,
            Location = new EventLocationV1
            {
                Start = new PitchPointV1 { X = .25m, Y = .5m },
                End = new PitchPointV1 { X = .75m, Y = .5m }
            },
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
        Assert.Equal(7, restored.CanonicalRevision);
        Assert.Equal(0, restored.Sequence);
        Assert.Equal("original-event-01", restored.Source.SourceRecordId);
        Assert.Equal(1, restored.Source.SourceSequence);
        Assert.Equal("ProgressivePass", restored.SourceEventType);
        Assert.Equal(.25m, restored.Location!.Start!.X);
        Assert.Equal(.75m, restored.Location.End!.X);
        Assert.Equal(5761, restored.MatchTime.ElapsedSeconds);
        Assert.Equal(5761, restored.MatchTime.SourceClockValue);
        Assert.True(TelemetryContractValidatorV1.Validate(restored).IsValid);
    }

    [Fact]
    public void Canonical_revision_is_distinct_from_schema_version_on_event_and_frame_contracts()
    {
        var eventValue = Event("Pass") with { CanonicalRevision = 7 };
        var frameValue = Frame() with { CanonicalRevision = 9 };

        using var eventJson = JsonDocument.Parse(JsonSerializer.Serialize(eventValue, JsonOptions));
        using var frameJson = JsonDocument.Parse(JsonSerializer.Serialize(frameValue, JsonOptions));

        Assert.Equal(1, eventJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(7, eventJson.RootElement.GetProperty("canonicalRevision").GetInt64());
        Assert.Equal(1, frameJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(9, frameJson.RootElement.GetProperty("canonicalRevision").GetInt64());
        Assert.True(TelemetryContractValidatorV1.Validate(eventValue).IsValid);
        Assert.True(TelemetryContractValidatorV1.Validate(frameValue).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Precanonical_or_invalid_revision_cannot_validate_as_canonical_telemetry(long revision)
    {
        var eventErrors = TelemetryContractValidatorV1.Validate(Event("Pass") with { CanonicalRevision = revision }).Errors;
        var frameErrors = TelemetryContractValidatorV1.Validate(Frame() with { CanonicalRevision = revision }).Errors;

        Assert.Contains(eventErrors, error => error.Code == "INVALID_CANONICAL_REVISION" && error.Path == "canonicalRevision");
        Assert.Contains(frameErrors, error => error.Code == "INVALID_CANONICAL_REVISION" && error.Path == "canonicalRevision");
    }

    [Fact]
    public void Canonical_contract_deserialization_rejects_pre_revision_source_records()
    {
        var eventNode = JsonNode.Parse(JsonSerializer.Serialize(Event("Pass"), JsonOptions))!.AsObject();
        eventNode.Remove("canonicalRevision");
        var frameNode = JsonNode.Parse(JsonSerializer.Serialize(Frame(), JsonOptions))!.AsObject();
        frameNode.Remove("canonicalRevision");

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EventEnvelopeV1>(eventNode.ToJsonString(), JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackingFrameV1>(frameNode.ToJsonString(), JsonOptions));
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
        Assert.Contains(eventSchema.RootElement.GetProperty("required").EnumerateArray(), field => field.GetString() == "canonicalRevision");
        Assert.Equal(1, eventSchema.RootElement.GetProperty("properties").GetProperty("canonicalRevision").GetProperty("minimum").GetInt32());
        Assert.Equal(long.MaxValue, eventSchema.RootElement.GetProperty("properties").GetProperty("canonicalRevision").GetProperty("maximum").GetInt64());
        Assert.Equal("(?:Z|[+-]00:00)$", eventSchema.RootElement.GetProperty("$defs").GetProperty("source").GetProperty("properties").GetProperty("sourceTimestampUtc").GetProperty("pattern").GetString());
        Assert.Equal(".*\\S.*", frameSchema.RootElement.GetProperty("properties").GetProperty("matchId").GetProperty("pattern").GetString());
        Assert.Contains(frameSchema.RootElement.GetProperty("required").EnumerateArray(), field => field.GetString() == "canonicalRevision");
        Assert.Equal(1, frameSchema.RootElement.GetProperty("properties").GetProperty("canonicalRevision").GetProperty("minimum").GetInt32());
        Assert.Equal(long.MaxValue, frameSchema.RootElement.GetProperty("properties").GetProperty("canonicalRevision").GetProperty("maximum").GetInt64());
        Assert.Contains(eventSchema.RootElement.GetProperty("allOf").EnumerateArray(), rule =>
            rule.GetProperty("if").GetProperty("properties").GetProperty("eventType").GetProperty("const").GetString() == "Substitution" &&
            rule.GetProperty("then").GetProperty("required").EnumerateArray().Any(field => field.GetString() == "relatedPlayerId") &&
            rule.GetProperty("then").GetProperty("properties").GetProperty("relatedPlayerId").GetProperty("type").GetString() == "string");
        Assert.Equal(90, frameSchema.RootElement.GetProperty("$defs").GetProperty("coordinateSystem").GetProperty("properties").GetProperty("lengthMetres").GetProperty("minimum").GetInt32());
        Assert.Equal(120, frameSchema.RootElement.GetProperty("$defs").GetProperty("coordinateSystem").GetProperty("properties").GetProperty("lengthMetres").GetProperty("maximum").GetInt32());
        Assert.True(eventSchema.RootElement.GetProperty("$defs").GetProperty("eventLocation").TryGetProperty("anyOf", out _));
        Assert.Equal(64, eventSchema.RootElement.GetProperty("$defs").GetProperty("coordinateSystem").GetProperty("properties").GetProperty("teamDirections").GetProperty("maxItems").GetInt32());
        Assert.Equal("null", eventSchema.RootElement.GetProperty("$defs").GetProperty("eventLocation").GetProperty("properties").GetProperty("end").GetProperty("anyOf")[1].GetProperty("type").GetString());
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
    public void Event_locations_require_declared_coordinates_and_normalize_under_bounded_pitch_dimensions()
    {
        var coordinateSystem = Frame().CoordinateSystem;
        var value = Event("Pass") with
        {
            CoordinateSystem = coordinateSystem,
            Location = new EventLocationV1
            {
                Start = new PitchPointV1 { X = .25m, Y = .5m },
                End = new PitchPointV1 { X = .75m, Y = .5m }
            }
        };

        Assert.True(TelemetryContractValidatorV1.Validate(value).IsValid);
        Assert.Equal(26.25m, TelemetryContractValidatorV1.NormalizeToStadiumMetres(value.Location!.Start!, coordinateSystem).XMetres);

        var missingSystem = TelemetryContractValidatorV1.Validate(value with { CoordinateSystem = null }).Errors;
        Assert.Contains(missingSystem, error => error.Code == "COORDINATE_SYSTEM_REQUIRED");

        var badPoint = value with { Location = value.Location with { End = new PitchPointV1 { X = 1.01m, Y = .5m } } };
        Assert.Contains(TelemetryContractValidatorV1.Validate(badPoint).Errors, error => error.Code == "INVALID_COORDINATE");

        var missingPoints = value with { Location = new EventLocationV1() };
        Assert.Contains(TelemetryContractValidatorV1.Validate(missingPoints).Errors, error => error.Code == "EVENT_LOCATION_REQUIRED");

        var oneSided = value with { Location = new EventLocationV1 { Start = new PitchPointV1 { X = .25m, Y = .5m } } };
        var restored = JsonSerializer.Deserialize<EventEnvelopeV1>(JsonSerializer.Serialize(oneSided, JsonOptions), JsonOptions)!;
        Assert.Null(restored.Location!.End);
        Assert.True(TelemetryContractValidatorV1.Validate(restored).IsValid);

        var invalidDirections = value with
        {
            CoordinateSystem = coordinateSystem with
            {
                TeamDirections = new[]
                {
                    new TeamAttackingDirectionV1 { TeamId = " ", Direction = "north" },
                    new TeamAttackingDirectionV1 { TeamId = "home", Direction = "left" },
                    new TeamAttackingDirectionV1 { TeamId = "home", Direction = "right" }
                }
            }
        };
        var directionErrors = TelemetryContractValidatorV1.Validate(invalidDirections).Errors;
        Assert.Contains(directionErrors, error => error.Path == "coordinateSystem.teamDirections[0].teamId");
        Assert.Contains(directionErrors, error => error.Code == "UNKNOWN_ATTACK_DIRECTION");
        Assert.Contains(directionErrors, error => error.Code == "DUPLICATE_TEAM_DIRECTION");

        var invalidDimensions = value with { CoordinateSystem = coordinateSystem with { LengthMetres = 89 } };
        Assert.Contains(TelemetryContractValidatorV1.Validate(invalidDimensions).Errors, error => error.Code == "INVALID_PITCH_DIMENSION");
    }

    [Theory]
    [InlineData(89, 68)]
    [InlineData(121, 68)]
    [InlineData(105, 44)]
    [InlineData(105, 91)]
    public void Tracking_frame_rejects_pitch_dimensions_outside_declared_football_bounds(decimal lengthMetres, decimal widthMetres)
    {
        var frame = Frame() with
        {
            CoordinateSystem = Frame().CoordinateSystem with { LengthMetres = lengthMetres, WidthMetres = widthMetres }
        };

        Assert.Single(TelemetryContractValidatorV1.Validate(frame).Errors, error => error.Code == "INVALID_PITCH_DIMENSION");
    }

    [Theory]
    [InlineData(90, 45)]
    [InlineData(120, 90)]
    public void Tracking_frame_accepts_pitch_dimension_boundaries(decimal lengthMetres, decimal widthMetres)
    {
        var frame = Frame() with
        {
            CoordinateSystem = Frame().CoordinateSystem with { LengthMetres = lengthMetres, WidthMetres = widthMetres }
        };

        Assert.True(TelemetryContractValidatorV1.Validate(frame).IsValid);
    }

    [Theory]
    [InlineData(90, 45)]
    [InlineData(120, 90)]
    public void Event_location_accepts_pitch_dimension_boundaries(decimal lengthMetres, decimal widthMetres)
    {
        var value = Event("Pass") with
        {
            CoordinateSystem = Frame().CoordinateSystem with { LengthMetres = lengthMetres, WidthMetres = widthMetres },
            Location = new EventLocationV1 { Start = new PitchPointV1 { X = .5m, Y = .5m } }
        };

        Assert.True(TelemetryContractValidatorV1.Validate(value).IsValid);
    }

    [Fact]
    public void Tracking_frame_bounds_team_direction_count()
    {
        var frame = Frame() with
        {
            CoordinateSystem = Frame().CoordinateSystem with
            {
                TeamDirections = Enumerable.Range(0, TelemetryContractValidatorV1.MaximumPlayersPerFrame + 1)
                    .Select(index => new TeamAttackingDirectionV1 { TeamId = $"team-{index}", Direction = "left" }).ToArray()
            }
        };

        Assert.Contains(TelemetryContractValidatorV1.Validate(frame).Errors, error => error.Code == "TOO_MANY_TEAM_DIRECTIONS");
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
        CanonicalRevision = 1,
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
        CanonicalRevision = 1,
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
