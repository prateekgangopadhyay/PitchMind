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
