using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PitchMind.Api;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Simulation;
using PitchMind.Infrastructure.Storage;
using PitchMind.TestFixtures;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class V1ContractCompatibilityTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Local_runtime_profile_is_allowed_only_in_Development(string environmentName)
    {
        RuntimeProfileGuard.Validate(environmentName, "local");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Azure_runtime_profile_is_allowed_in_supported_host_environments(string environmentName)
    {
        RuntimeProfileGuard.Validate(environmentName, "azure");
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("Development", "unknown")]
    [InlineData("Local", "local")]
    [InlineData("Staging", "local")]
    [InlineData("Production", "local")]
    [InlineData("Test", "azure")]
    public void Runtime_profile_guard_rejects_missing_or_incompatible_profiles(string environmentName, string? runtimeProfile)
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeProfileGuard.Validate(environmentName, runtimeProfile));
    }

    [Fact]
    public void Local_in_memory_store_is_registered_only_for_the_local_profile()
    {
        var localServices = new ServiceCollection();
        RuntimeProfileGuard.ConfigureLocalServices(localServices, "local");
        Assert.Contains(localServices, descriptor => descriptor.ServiceType == typeof(IMatchEventStore) &&
            descriptor.ImplementationType == typeof(InMemoryMatchEventStore));

        var azureServices = new ServiceCollection();
        RuntimeProfileGuard.ConfigureLocalServices(azureServices, "azure");
        Assert.DoesNotContain(azureServices, descriptor => descriptor.ServiceType == typeof(IMatchEventStore));
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Fixture_serialization_preserves_the_v1_consumer_shape()
    {
        var fixture = new FixtureResponse("match", "source-v1", 0, "synthetic", 0, 0, 105, 68,
            new TeamResponse("home", "Home", "unknown", Array.Empty<PlayerResponse>()),
            new TeamResponse("away", "Away", "unknown", Array.Empty<PlayerResponse>()),
            Array.Empty<SimulationEventResponse>(), Array.Empty<TrackingFrameResponse>());

        AssertJsonProperties(fixture, "matchId", "version", "seed", "provenance", "durationSeconds", "halfTimeSeconds", "pitchLengthMetres", "pitchWidthMetres", "home", "away", "events", "frames");
        Assert.Equal("source-v1", fixture.Version);
        Assert.Equal("1", ContractVersion.V1);
    }

    [Fact]
    public void Analyst_request_serialization_pins_the_v1_request_shape()
    {
        var request = new AnalystRequest("insight", 5400, "analyst");
        var requestWithoutAudience = new AnalystRequest("insight", 0);

        AssertJsonProperties(request, "insightId", "atSeconds", "audience");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, Json));
        Assert.Equal("insight", json.RootElement.GetProperty("insightId").GetString());
        Assert.Equal(5400, json.RootElement.GetProperty("atSeconds").GetInt32());
        Assert.Equal("analyst", json.RootElement.GetProperty("audience").GetString());
        using var withoutAudienceJson = JsonDocument.Parse(JsonSerializer.Serialize(requestWithoutAudience, Json));
        Assert.DoesNotContain("audience", withoutAudienceJson.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Match_and_evidence_serialization_preserve_required_identifiers_and_time()
    {
        var sourceEvent = new MatchEventResponse(Guid.Empty, "match", 0, "pass", 0, 1, DateTimeOffset.UnixEpoch);
        var simulationEvent = new SimulationEventResponse(sourceEvent, "team", "player", "action", "outcome", null);
        var frame = new TrackingFrameResponse("frame", 0, 0, 0, "offline synthetic fixture coordinate convention", new[] { new PitchPositionResponse("player", "team", 0, 0) });
        var context = new TacticalContextResponse("tactical-context-v1", "home 1–0 away", "home", 0,
            "opponent in possession at trigger; defending after observed regain", "SecondHalf");
        var insight = new InsightResponse("insight", "match", 0, "title", "summary", "classification", "detector", new[] { Guid.Empty }, new[] { "frame" },
            "synthetic-v2", new string('a', 64), "synthetic", null, 0, -1, "unapproved-demo", false, Array.Empty<string>(), context, null);
        var evidence = new EvidenceResponse(insight, new[] { simulationEvent }, new[] { frame }, "coordinate convention", Array.Empty<string>(), Array.Empty<string>());
        var explanation = new ExplanationResponse("insight", 0, "fan", "deterministic-fallback", "text", new[] { Guid.Empty }, Array.Empty<string>());
        var problem = new ApiProblemResponse("about:blank", "Bad request", 400, "INVALID_REQUEST", "correlation", Instance: "/api/v1/matches");

        AssertJsonProperties(sourceEvent, "eventId", "matchId", "sequence", "type", "clockSeconds", "period", "occurredAt");
        AssertJsonProperties(simulationEvent, "event", "teamId", "playerId", "action", "outcome", "relatedPlayerId");
        AssertJsonProperties(frame, "id", "clockSeconds", "ballX", "ballY", "coordinateConvention", "players");
        AssertJsonProperties(frame.Players[0], "playerId", "teamId", "x", "y");
        AssertJsonProperties(insight, "id", "matchId", "clockSeconds", "title", "summary", "classification", "detectorVersion", "eventIds", "frameIds",
            "sourceVersion", "sourceSha256", "provenance", "canonicalRevision", "selectedTimeSeconds", "throughSequence", "policyStatus", "publishable", "conditions", "context", "evidenceGraph");
        AssertJsonProperties(context, "schemaVersion", "scoreline", "teamRole", "numericalAdvantage", "opponentPhase", "matchPhase");
        AssertJsonProperties(new EvidenceGraphResponse("match", "synthetic-v2", new string('a', 64), "synthetic", null, 0, -1,
                "press-sequence-v2", "unapproved-demo", false, "claim:demo", Array.Empty<EvidenceNodeResponse>(), Array.Empty<EvidenceEdgeResponse>(),
                "uncalibrated", Array.Empty<EvidenceConditionResponse>()),
            "matchId", "sourceVersion", "sourceSha256", "provenance", "canonicalRevision", "selectedTimeSeconds", "throughSequence",
            "detectorVersion", "policyStatus", "publishable", "claimNodeId", "nodes", "edges", "uncertainty", "conditions");
        AssertJsonProperties(new EvidenceNodeResponse("event:demo", "event", "fact", "Synthetic event", Guid.Empty, null,
                0, 0, "synthetic-v2", new string('a', 64), null, null, null),
            "id", "kind", "truthClass", "description", "eventId", "frameId", "clockSeconds", "sequence", "sourceVersion", "sourceSha256", "canonicalRevision", "numericValue", "unit");
        AssertJsonProperties(new EvidenceEdgeResponse("claim:demo", "event:demo", "derived_from"),
            "fromNodeId", "toNodeId", "relation");
        AssertJsonProperties(new EvidenceConditionResponse("policy", false, "unapproved", "approved", Array.Empty<string>(), "Pending review"),
            "id", "passed", "observed", "threshold", "sourceNodeIds", "exclusionReason");
        AssertJsonProperties(evidence, "insight", "events", "frames", "coordinateConvention", "conditions", "limitations");
        AssertJsonProperties(explanation, "insightId", "clockSeconds", "audience", "source", "text", "eventIds", "limitations");
        AssertJsonProperties(problem, "type", "title", "status", "code", "correlationId", "detail", "instance");

        using var eventJson = JsonDocument.Parse(JsonSerializer.Serialize(sourceEvent, Json));
        Assert.Equal(JsonValueKind.String, eventJson.RootElement.GetProperty("eventId").ValueKind);
        Assert.Equal(JsonValueKind.Number, eventJson.RootElement.GetProperty("sequence").ValueKind);
        Assert.Equal(JsonValueKind.Number, eventJson.RootElement.GetProperty("clockSeconds").ValueKind);
        Assert.Equal(JsonValueKind.String, eventJson.RootElement.GetProperty("occurredAt").ValueKind);
        using var simulationJson = JsonDocument.Parse(JsonSerializer.Serialize(simulationEvent, Json));
        Assert.Equal(JsonValueKind.Null, simulationJson.RootElement.GetProperty("relatedPlayerId").ValueKind);
        using var problemJson = JsonDocument.Parse(JsonSerializer.Serialize(problem, Json));
        Assert.Equal(JsonValueKind.Number, problemJson.RootElement.GetProperty("status").ValueKind);
    }

    [Fact]
    public void State_response_exposes_selected_time_projection_fields()
    {
        var fixture = SyntheticFixtureGenerator.GenerateScenario(seed: 42).Fixture;
        fixture = fixture with
        {
            CompetitionId = "synthetic-competition",
            CompetitionRulesVersion = "rules-v1",
            MaximumSubstituteReentriesPerPlayer = 0
        };
        var state = new SyntheticMatchService(fixture).GetState(3600).ToResponse();
        AssertJsonProperties(state, "matchId", "clockSeconds", "period", "status", "phase", "homeScore", "awayScore",
            "provenance", "frame", "lastSequence", "possessionTeamId",
            "homeActivePlayers", "awayActivePlayers", "players", "competitionId", "competitionRulesVersion",
            "maximumSubstituteReentriesPerPlayer");
        Assert.Equal("replay", state.Status);
        Assert.Equal("secondhalf", state.Phase);
        Assert.Equal("synthetic-competition", state.CompetitionId);
        Assert.Equal("rules-v1", state.CompetitionRulesVersion);
        Assert.Equal(0, state.MaximumSubstituteReentriesPerPlayer);
        Assert.False(state.Players.Single(player => player.PlayerId == "harbour-7").Active);
        Assert.True(state.Players.Single(player => player.PlayerId == "harbour-12").Active);
    }

    [Fact]
    public void Replay_contract_pins_versions_and_keeps_canonical_revision_explicitly_unavailable_offline()
    {
        var replay = new SyntheticMatchService(SyntheticFixtureGenerator.Generate()).ReplayAt(3308).ToResponse();

        AssertJsonProperties(replay, "executionMode", "matchId", "selectedElapsedSeconds", "fixtureVersion", "fixtureSeed",
            "replayAlgorithmVersion", "detectorVersion", "replayInputSha256", "replayOutputSha256", "canonicalRevision",
            "state", "events", "insights");
        Assert.Equal("offline-synthetic-fixture", replay.ExecutionMode);
        Assert.Null(replay.CanonicalRevision);
        Assert.Equal(64, replay.ReplayInputSha256.Length);
        Assert.Equal(64, replay.ReplayOutputSha256.Length);
        Assert.All(replay.Events, item => Assert.True(item.Event.ClockSeconds <= replay.SelectedElapsedSeconds));
        Assert.All(replay.Insights, item => Assert.True(item.ClockSeconds <= replay.SelectedElapsedSeconds));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(replay, Json));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("canonicalRevision").ValueKind);
    }

    [Fact]
    public void Insight_mapping_carries_source_provenance_and_fingerprint()
    {
        var insight = new SyntheticMatchService(SyntheticFixtureGenerator.Generate())
            .GetInsights(3309).Single().ToResponse();

        Assert.Equal("synthetic", insight.Provenance);
        Assert.Matches("^[0-9a-f]{64}$", insight.SourceSha256);
        Assert.Equal("synthetic-v3", insight.SourceVersion);
        Assert.False(insight.Publishable);
    }

    [Fact]
    public void Key_moment_contract_carries_selected_time_relevance_and_why_references()
    {
        var service = new PitchMind.Application.Matches.SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var moment = service.GetKeyMoments(3309).Single(item => item.Kind == "goal").ToResponse();

        AssertJsonProperties(moment, "id", "matchId", "sourceVersion", "sourceSha256", "provenance", "canonicalRevision", "selectedTimeSeconds",
            "throughSequence", "clockSeconds", "sequence", "kind", "truthClass", "title", "summary", "relevance",
            "policyVersion", "policyStatus", "publishable", "eventIds", "frameIds", "evidenceClaimNodeIds");
        Assert.Equal(3309, moment.SelectedTimeSeconds);
        Assert.Equal(PitchMind.Domain.Analytics.PressSequenceDetector.ComputeSourceSha256(service.Fixture), moment.SourceSha256);
        Assert.Single(moment.EventIds);
        Assert.False(moment.Publishable);
    }

    [Fact]
    public void Mechanism_replay_contract_preserves_selected_evidence_and_limits()
    {
        var service = new PitchMind.Application.Matches.SyntheticMatchService(SyntheticFixtureGenerator.Generate());
        var insight = service.GetInsights(3309).Single();
        var replay = service.GetMechanismReplay(insight.Id, 3309)!.ToResponse();

        AssertJsonProperties(replay, "id", "matchId", "sourceVersion", "sourceSha256", "provenance", "canonicalRevision",
            "selectedTimeSeconds", "throughSequence", "startSeconds", "endSeconds", "detectorVersion",
            "policyStatus", "publishable", "eventIds", "mechanismEventIds", "frameIds", "participantIds", "events", "frames", "coordinateConvention",
            "evidenceGraph", "limitations");
        Assert.Equal(insight.EventIds, replay.MechanismEventIds);
        Assert.Equal(insight.SourceSha256, replay.SourceSha256);
        Assert.Equal(replay.EvidenceGraph.SourceSha256, replay.SourceSha256);
        Assert.Equal(
            replay.EvidenceGraph.Nodes.Where(node => node.Kind == "event").Select(node => node.EventId).Order().ToArray(),
            replay.EventIds.Select(id => (Guid?)id).Order().ToArray());
        Assert.Equal(insight.FrameIds, replay.FrameIds);
        Assert.Equal(3309, replay.SelectedTimeSeconds);
        Assert.All(replay.Frames, frame => Assert.Equal(replay.CoordinateConvention, frame.CoordinateConvention));
        Assert.All(replay.Events, item => Assert.True(item.Event.ClockSeconds <= replay.SelectedTimeSeconds));
        Assert.All(replay.Frames, item => Assert.True(item.ClockSeconds <= replay.SelectedTimeSeconds));
        Assert.False(replay.Publishable);
    }

    [Fact]
    public void Canonical_BFF_contracts_carry_revision_time_provenance_and_stable_cursors()
    {
        var card = new CanonicalMatchCardResponse("match", 7L, 4, 3000, 42, "secondhalf", 1, 0,
            "synthetic", "aggregate-v1", new string('a', 64));
        var matchPage = new CanonicalMatchPageResponse(new[] { card }, "next-match", true);
        var player = new MatchPlayerStateResponse("player", "team", true, false, 0);
        var snapshot = new CanonicalMatchSnapshotResponse("match", 7L, 4, 3308, 3000, 42, "aggregate-v1",
            new string('a', 64), new string('b', 64), "synthetic", 2, "secondhalf", 1, 0, "team", 11, 11, new[] { player });
        var evt = new MatchEventResponse(Guid.NewGuid(), "match", 42, "Pass", 3000, 2, DateTimeOffset.UnixEpoch);
        var eventPage = new CanonicalEventPageResponse("match", 7L, 4, 3308, 42, new[] { evt }, "cursor", true);
        var evidence = new CanonicalEvidenceReferenceResponse("event", evt.EventId.ToString(), "supports");
        var insight = new CanonicalInsightResponse("insight", "match", 7L, 4, 3308, 42, "source-v1", "synthetic",
            new string('a', 64), "detector-v1", "derived", "Persisted summary",
            JsonSerializer.Deserialize<JsonElement>("{\"selected\":true}"), null, new[] { evidence });
        var insightPage = new CanonicalInsightPageResponse("match", 7L, 4, 3308, 42, new[] { insight }, "cursor", true);
        var evidencePayload = new CanonicalEvidencePayloadResponse("event", evt.EventId.ToString(), "supports", 6, 42,
            3000, 2, JsonSerializer.Deserialize<JsonElement>("{\"eventId\":\"sample\"}"));
        var canonicalEvidence = new CanonicalEvidenceResponse(insight, new[] { evidencePayload });
        var replay = new CanonicalReplayResponse(snapshot, eventPage);
        var trackingFrame = new CanonicalTrackingFrameResponse("frame-1", 43, 7, 4, 2, 3300,
            "pitch-v1", JsonSerializer.Deserialize<JsonElement>("{\"complete\":true}"), new string('a', 64),
            new PitchMind.Contracts.Telemetry.V1.TrackingFrameV1
            {
                SchemaVersion = 1,
                MatchId = Guid.NewGuid().ToString("D"),
                CanonicalRevision = 7,
                FrameId = "frame-1",
                Sequence = 43,
                MatchTime = new PitchMind.Contracts.Telemetry.V1.MatchTimeV1
                {
                    Period = 2, ElapsedSeconds = 3300, SourceClockValue = 3300,
                    SourceClockUnit = "seconds", SourceClockBasis = "elapsed"
                },
                CoordinateSystem = new PitchMind.Contracts.Telemetry.V1.PitchCoordinateSystemV1
                {
                    LengthMetres = 105, WidthMetres = 68, Unit = "metres", AxisOrigin = "bottom_left",
                    TeamDirections = Array.Empty<PitchMind.Contracts.Telemetry.V1.TeamAttackingDirectionV1>()
                },
                Ball = new PitchMind.Contracts.Telemetry.V1.PitchPointV1 { X = 52.5m, Y = 34m },
                Players = Array.Empty<PitchMind.Contracts.Telemetry.V1.PlayerPositionV1>(),
                Source = new PitchMind.Contracts.Telemetry.V1.SourceReferenceV1
                {
                    ProviderId = "provider", SourceMatchId = "source-match", SourceRecordId = "source-frame",
                    SourceSequence = 43, SourceSchemaVersion = "v1", SourceSha256 = new string('a', 64),
                    ProvenanceStatus = "synthetic", RightsReviewStatus = "reviewed"
                }
            });
        var trackingPage = new CanonicalTrackingFramePageResponse(Guid.NewGuid().ToString("D"), 7, 4, 3300,
            50, new[] { trackingFrame }, "opaque-cursor", true);

        AssertJsonProperties(card, "matchId", "canonicalRevision", "projectionRevision", "asOfSeconds", "throughSequence", "phase",
            "homeScore", "awayScore", "provenance", "algorithmVersion", "sourceSha256");
        AssertJsonProperties(matchPage, "items", "nextMatchId", "hasMore");
        AssertJsonProperties(snapshot, "matchId", "canonicalRevision", "projectionRevision", "selectedTimeSeconds", "asOfSeconds",
            "throughSequence", "algorithmVersion", "sourceSha256", "stateSha256", "provenance", "period", "phase",
            "homeScore", "awayScore", "possessionTeamId", "homeActivePlayers", "awayActivePlayers", "players");
        AssertJsonProperties(eventPage, "matchId", "canonicalRevision", "projectionRevision", "selectedTimeSeconds", "throughSequence",
            "items", "nextCursor", "hasMore");
        AssertJsonProperties(insight, "insightId", "matchId", "canonicalRevision", "projectionRevision", "selectedTimeSeconds",
            "throughSequence", "sourceVersion", "provenance", "sourceSha256", "detectorVersion", "truthClass", "summary",
            "conditions", "limitations", "evidence");
        Assert.Null(insight.Limitations);
        AssertJsonProperties(evidence, "kind", "id", "relation");
        AssertJsonProperties(insightPage, "matchId", "canonicalRevision", "projectionRevision", "selectedTimeSeconds", "throughSequence",
            "items", "nextCursor", "hasMore");
        AssertJsonProperties(evidencePayload, "kind", "id", "relation", "sourceCanonicalRevision", "sequence", "clockSeconds", "period", "payload");
        AssertJsonProperties(canonicalEvidence, "insight", "items");
        AssertJsonProperties(replay, "snapshot", "events");
        AssertJsonProperties(trackingFrame, "frameId", "sequence", "canonicalRevision", "projectionRevision", "period",
            "clockSeconds", "coordinateVersion", "quality", "sourceSha256", "frame");
        AssertJsonProperties(trackingPage, "matchId", "canonicalRevision", "projectionRevision", "selectedTimeSeconds",
            "throughSequence", "items", "nextCursor", "hasMore");
    }

    [Fact]
    public void Domain_mapping_preserves_original_event_identity_sequence_and_occurrence_time()
    {
        var source = SyntheticFixtureGenerator.Generate() with
        {
            PitchLengthMetres = 110,
            PitchWidthMetres = 75
        };
        var mapped = source.ToResponse();

        Assert.Equal(source.Version, mapped.Version);
        Assert.NotEqual(ContractVersion.V1, mapped.Version);
        Assert.Equal(source.DurationSeconds, mapped.DurationSeconds);
        Assert.Equal(source.HalfTimeSeconds, mapped.HalfTimeSeconds);
        Assert.Equal(source.PitchLengthMetres, mapped.PitchLengthMetres);
        Assert.Equal(source.PitchWidthMetres, mapped.PitchWidthMetres);
        Assert.Equal(source.CoordinateConvention, mapped.Frames[0].CoordinateConvention);
        var state = new SyntheticMatchService(source).GetState(source.DurationSeconds).ToResponse();
        Assert.Equal(mapped.Frames[0].CoordinateConvention, state.Frame.CoordinateConvention);
        Assert.Equal(source.Events.Count, mapped.Events.Count);
        for (var index = 0; index < source.Events.Count; index++)
        {
            var original = source.Events[index].Event;
            var contract = mapped.Events[index].Event;
            Assert.Equal(original.EventId, contract.EventId);
            Assert.Equal(original.Sequence, contract.Sequence);
            Assert.Equal(original.OccurredAt, contract.OccurredAt);
            Assert.Equal(original.ClockSeconds, contract.ClockSeconds);
        }
    }

    private static void AssertJsonProperties<T>(T value, params string[] expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, Json));
        Assert.Equal(expected.Order(), document.RootElement.EnumerateObject().Select(property => property.Name).Order());
    }
}
