using System.Text.Json;
using PitchMind.Application.Matches;
using PitchMind.Api;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class V1ContractCompatibilityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
    public void Match_and_evidence_serialization_preserve_required_identifiers_and_time()
    {
        var sourceEvent = new MatchEventResponse(Guid.Empty, "match", 0, "pass", 0, 1, DateTimeOffset.UnixEpoch);
        var simulationEvent = new SimulationEventResponse(sourceEvent, "team", "player", "action", "outcome", null);
        var frame = new TrackingFrameResponse("frame", 0, 0, 0, "offline synthetic fixture coordinate convention", new[] { new PitchPositionResponse("player", "team", 0, 0) });
        var insight = new InsightResponse("insight", "match", 0, "title", "summary", "classification", "detector", new[] { Guid.Empty }, new[] { "frame" },
            "synthetic-v2", null, 0, -1, "unapproved-demo", false, Array.Empty<string>(), null, null);
        var evidence = new EvidenceResponse(insight, new[] { simulationEvent }, new[] { frame }, "coordinate convention", Array.Empty<string>(), Array.Empty<string>());
        var explanation = new ExplanationResponse("insight", 0, "fan", "deterministic-fallback", "text", new[] { Guid.Empty }, Array.Empty<string>());
        var problem = new ApiProblemResponse("about:blank", "Bad request", 400, "INVALID_REQUEST", "correlation", Instance: "/api/v1/matches");

        AssertJsonProperties(sourceEvent, "eventId", "matchId", "sequence", "type", "clockSeconds", "period", "occurredAt");
        AssertJsonProperties(simulationEvent, "event", "teamId", "playerId", "action", "outcome", "relatedPlayerId");
        AssertJsonProperties(frame, "id", "clockSeconds", "ballX", "ballY", "coordinateConvention", "players");
        AssertJsonProperties(frame.Players[0], "playerId", "teamId", "x", "y");
        AssertJsonProperties(insight, "id", "matchId", "clockSeconds", "title", "summary", "classification", "detectorVersion", "eventIds", "frameIds",
            "sourceVersion", "canonicalRevision", "selectedTimeSeconds", "throughSequence", "policyStatus", "publishable", "conditions", "context", "evidenceGraph");
        AssertJsonProperties(new TacticalContextResponse("home 1–0 away", "home", 0, "opponent defending", "SecondHalf"),
            "scoreline", "teamRole", "numericalAdvantage", "opponentPhase", "matchPhase");
        AssertJsonProperties(new EvidenceGraphResponse("match", "synthetic-v2", SourceHash, "synthetic", null, 0, -1,
                "press-sequence-v2", "unapproved-demo", false, "claim:demo", Array.Empty<EvidenceNodeResponse>(),
                Array.Empty<EvidenceEdgeResponse>(), "No calibrated confidence score.", Array.Empty<EvidenceConditionResponse>()),
            "matchId", "sourceVersion", "sourceSha256", "provenance", "canonicalRevision", "selectedTimeSeconds", "throughSequence",
            "detectorVersion", "policyStatus", "publishable", "claimNodeId", "nodes", "edges", "uncertainty", "conditions");
        AssertJsonProperties(new EvidenceNodeResponse("event:demo", "event", "fact", "Synthetic event", Guid.Empty, null,
                0, 0, "synthetic-v2", SourceHash, null, null, null),
            "id", "kind", "truthClass", "description", "eventId", "frameId", "clockSeconds", "sequence", "sourceVersion", "sourceSha256", "canonicalRevision", "numericValue", "unit");
        AssertJsonProperties(new EvidenceEdgeResponse("claim:demo", "event:demo", "derived_from"),
            "fromNodeId", "toNodeId", "relation");
        AssertJsonProperties(new EvidenceConditionResponse("pressure", true, "2m separation", "<=3m",
                new[] { "event:demo", "frame:demo" }, null),
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
        var state = new PitchMind.Application.Matches.SyntheticMatchService().GetState(3600).ToResponse();
        AssertJsonProperties(state, "matchId", "clockSeconds", "period", "status", "phase", "homeScore", "awayScore",
            "provenance", "frame", "lastSequence", "possessionTeamId",
            "homeActivePlayers", "awayActivePlayers", "players");
        Assert.Equal("replay", state.Status);
        Assert.Equal("secondhalf", state.Phase);
        Assert.False(state.Players.Single(player => player.PlayerId == "harbour-7").Active);
        Assert.True(state.Players.Single(player => player.PlayerId == "harbour-12").Active);
    }

    [Fact]
    public void Insight_evidence_graph_exposes_the_source_hash_on_graph_and_nodes()
    {
        var insight = new SyntheticMatchService().GetInsights(5400).Single().ToResponse();
        var graph = Assert.IsType<EvidenceGraphResponse>(insight.EvidenceGraph);

        Assert.Matches("^[a-f0-9]{64}$", graph.SourceSha256);
        Assert.NotEmpty(graph.Nodes);
        Assert.All(graph.Nodes, node => Assert.Equal(graph.SourceSha256, node.SourceSha256));
        Assert.NotEmpty(graph.Conditions);
        Assert.Contains(graph.Conditions, condition => condition.Id == "publication-policy" && !condition.Passed &&
            condition.ExclusionReason is not null);
        Assert.All(graph.Conditions.SelectMany(condition => condition.SourceNodeIds), sourceNodeId =>
            Assert.Contains(graph.Nodes, node => node.Id == sourceNodeId));
        Assert.Contains("no confidence score", graph.Uncertainty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(graph.Nodes, node => node.TruthClass == "pattern");
        Assert.Contains(graph.Nodes, node => node.TruthClass == "fact");
        Assert.Contains(graph.Nodes, node => node.TruthClass == "derivation");
    }

    [Fact]
    public void Key_moment_contract_carries_selected_time_relevance_and_why_references()
    {
        var service = new PitchMind.Application.Matches.SyntheticMatchService();
        var moment = service.GetKeyMoments(3309).Single(item => item.Kind == "goal").ToResponse();

        AssertJsonProperties(moment, "id", "matchId", "sourceVersion", "provenance", "canonicalRevision", "selectedTimeSeconds",
            "throughSequence", "clockSeconds", "sequence", "kind", "truthClass", "title", "summary", "relevance",
            "policyVersion", "policyStatus", "publishable", "eventIds", "frameIds", "evidenceClaimNodeIds");
        Assert.Equal(3309, moment.SelectedTimeSeconds);
        Assert.Single(moment.EventIds);
        Assert.False(moment.Publishable);
    }

    [Fact]
    public void Mechanism_replay_contract_preserves_selected_evidence_and_limits()
    {
        var service = new PitchMind.Application.Matches.SyntheticMatchService();
        var insight = service.GetInsights(3309).Single();
        var replay = service.GetMechanismReplay(insight.Id, 3309)!.ToResponse();

        AssertJsonProperties(replay, "id", "matchId", "sourceVersion", "provenance", "canonicalRevision",
            "selectedTimeSeconds", "throughSequence", "startSeconds", "endSeconds", "detectorVersion",
            "policyStatus", "publishable", "eventIds", "mechanismEventIds", "frameIds", "participantIds", "events", "frames", "coordinateConvention",
            "evidenceGraph", "limitations");
        Assert.Equal(insight.EventIds, replay.MechanismEventIds);
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
        var card = new CanonicalMatchCardResponse("match", 4, 3000, 42, "secondhalf", 1, 0,
            "synthetic", "aggregate-v1", new string('a', 64));
        var matchPage = new CanonicalMatchPageResponse(new[] { card }, "next-match", true);
        var player = new MatchPlayerStateResponse("player", "team", true, false, 0);
        var snapshot = new CanonicalMatchSnapshotResponse("match", 4, 3308, 3000, 42, "aggregate-v1",
            new string('a', 64), new string('b', 64), "synthetic", 2, "secondhalf", 1, 0, "team", 11, 11, new[] { player });
        var evt = new MatchEventResponse(Guid.NewGuid(), "match", 42, "Pass", 3000, 2, DateTimeOffset.UnixEpoch);
        var eventPage = new CanonicalEventPageResponse("match", 4, 3308, 42, new[] { evt }, "cursor", true);
        var evidence = new CanonicalEvidenceReferenceResponse("event", evt.EventId.ToString(), "supports");
        var insight = new CanonicalInsightResponse("insight", "match", 4, 3308, 42, "source-v1", "synthetic",
            new string('a', 64), "detector-v1", "derived", "Persisted summary", new[] { evidence });
        var insightPage = new CanonicalInsightPageResponse("match", 4, 3308, 42, new[] { insight }, "cursor", true);
        var evidencePayload = new CanonicalEvidencePayloadResponse("event", evt.EventId.ToString(), "supports", 42,
            3000, 2, JsonSerializer.Deserialize<JsonElement>("{\"eventId\":\"sample\"}"));
        var canonicalEvidence = new CanonicalEvidenceResponse(insight, new[] { evidencePayload });
        var replay = new CanonicalReplayResponse(snapshot, eventPage);
        using var quality = JsonDocument.Parse("{} ");
        var trackingFramePayload = JsonSerializer.Deserialize<PitchMind.Contracts.Telemetry.V1.TrackingFrameV1>("""
            {"schemaVersion":1,"matchId":"match","frameId":"frame","sequence":42,
             "matchTime":{"period":2,"elapsedSeconds":3308,"sourceClockValue":3308,"sourceClockUnit":"seconds","sourceClockBasis":"match_elapsed"},
             "coordinateSystem":{"lengthMetres":105,"widthMetres":68,"unit":"metres","axisOrigin":"bottom_left","teamDirections":[{"teamId":"team","direction":"right"}]},
             "ball":{"x":52.5,"y":34},"players":[],
             "source":{"providerId":"test","sourceMatchId":"source-match","sourceRecordId":"frame","sourceSequence":9,
             "sourceSchemaVersion":"test-v1","sourceSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "provenanceStatus":"test","rightsReviewStatus":"test"}}
            """, Json)!;
        var trackingFrame = new CanonicalTrackingFrameResponse("frame", 42, 3, 2, 3308, "pitch-v1",
            new string('a', 64), quality.RootElement.Clone(), trackingFramePayload);
        var trackingPage = new CanonicalTrackingFramePageResponse("match", 4, 3308, 42, new[] { trackingFrame }, false);

        AssertJsonProperties(card, "matchId", "canonicalRevision", "asOfSeconds", "throughSequence", "phase",
            "homeScore", "awayScore", "provenance", "algorithmVersion", "sourceSha256");
        AssertJsonProperties(matchPage, "items", "nextMatchId", "hasMore");
        AssertJsonProperties(snapshot, "matchId", "canonicalRevision", "selectedTimeSeconds", "asOfSeconds",
            "throughSequence", "algorithmVersion", "sourceSha256", "stateSha256", "provenance", "period", "phase",
            "homeScore", "awayScore", "possessionTeamId", "homeActivePlayers", "awayActivePlayers", "players");
        AssertJsonProperties(eventPage, "matchId", "canonicalRevision", "selectedTimeSeconds", "throughSequence",
            "items", "nextCursor", "hasMore");
        AssertJsonProperties(insight, "insightId", "matchId", "canonicalRevision", "selectedTimeSeconds",
            "throughSequence", "sourceVersion", "provenance", "sourceSha256", "detectorVersion", "truthClass", "summary", "evidence");
        AssertJsonProperties(evidence, "kind", "id", "relation");
        AssertJsonProperties(insightPage, "matchId", "canonicalRevision", "selectedTimeSeconds", "throughSequence",
            "items", "nextCursor", "hasMore");
        AssertJsonProperties(evidencePayload, "kind", "id", "relation", "sequence", "clockSeconds", "period", "payload");
        AssertJsonProperties(canonicalEvidence, "insight", "items");
        AssertJsonProperties(replay, "snapshot", "events");
        AssertJsonProperties(trackingFrame, "frameId", "sequence", "canonicalRevision", "period", "clockSeconds",
            "coordinateVersion", "sourceSha256", "quality", "frame");
        AssertJsonProperties(trackingPage, "matchId", "canonicalRevision", "selectedTimeSeconds", "throughSequence", "items", "hasMore");
        Assert.Equal(3, trackingFrame.CanonicalRevision);
        Assert.Equal(new string('a', 64), trackingFrame.SourceSha256);
    }

    [Fact]
    public void Domain_mapping_preserves_original_event_identity_sequence_and_occurrence_time()
    {
        var source = SyntheticFixtureGenerator.Generate();
        var mapped = source.ToResponse();

        Assert.Equal(source.Version, mapped.Version);
        Assert.NotEqual(ContractVersion.V1, mapped.Version);
        Assert.Equal(source.DurationSeconds, mapped.DurationSeconds);
        Assert.Equal(source.HalfTimeSeconds, mapped.HalfTimeSeconds);
        Assert.Equal(source.PitchLengthMetres, mapped.PitchLengthMetres);
        Assert.Equal(source.PitchWidthMetres, mapped.PitchWidthMetres);
        Assert.Contains("x=0..105", mapped.Frames[0].CoordinateConvention);
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
