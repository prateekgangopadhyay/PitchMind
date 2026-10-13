using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PitchMind.Api;
using PitchMind.Application.Abstractions;
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
        var fixture = new FixtureResponse("match", "source-v1", 0, "synthetic", 0,
            new TeamResponse("home", "Home", "unknown", Array.Empty<PlayerResponse>()),
            new TeamResponse("away", "Away", "unknown", Array.Empty<PlayerResponse>()),
            Array.Empty<SimulationEventResponse>(), Array.Empty<TrackingFrameResponse>());

        AssertJsonProperties(fixture, "matchId", "version", "seed", "provenance", "durationSeconds", "home", "away", "events", "frames");
        Assert.Equal("source-v1", fixture.Version);
        Assert.Equal("1", ContractVersion.V1);
    }

    [Fact]
    public void Analyst_request_serialization_pins_the_v1_request_shape()
    {
        var request = new AnalystRequest("insight", 5400, "analyst");

        AssertJsonProperties(request, "insightId", "atSeconds", "audience");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, Json));
        Assert.Equal("insight", json.RootElement.GetProperty("insightId").GetString());
        Assert.Equal(5400, json.RootElement.GetProperty("atSeconds").GetInt32());
        Assert.Equal("analyst", json.RootElement.GetProperty("audience").GetString());
    }

    [Fact]
    public void Match_and_evidence_serialization_preserve_required_identifiers_and_time()
    {
        var sourceEvent = new MatchEventResponse(Guid.Empty, "match", 0, "pass", 0, 1, DateTimeOffset.UnixEpoch);
        var simulationEvent = new SimulationEventResponse(sourceEvent, "team", "player", "action", "outcome", null);
        var frame = new TrackingFrameResponse("frame", 0, 0, 0, "offline synthetic fixture coordinate convention", new[] { new PitchPositionResponse("player", "team", 0, 0) });
        var insight = new InsightResponse("insight", "match", 0, "title", "summary", "classification", "detector", new[] { Guid.Empty }, new[] { "frame" });
        var evidence = new EvidenceResponse(insight, new[] { simulationEvent }, new[] { frame }, "coordinate convention", Array.Empty<string>(), Array.Empty<string>());
        var explanation = new ExplanationResponse("insight", 0, "fan", "deterministic-fallback", "text", new[] { Guid.Empty }, Array.Empty<string>());
        var problem = new ApiProblemResponse("about:blank", "Bad request", 400, "INVALID_REQUEST", "correlation", Instance: "/api/v1/matches");

        AssertJsonProperties(sourceEvent, "eventId", "matchId", "sequence", "type", "clockSeconds", "period", "occurredAt");
        AssertJsonProperties(simulationEvent, "event", "teamId", "playerId", "action", "outcome", "relatedPlayerId");
        AssertJsonProperties(frame, "id", "clockSeconds", "ballX", "ballY", "coordinateConvention", "players");
        AssertJsonProperties(frame.Players[0], "playerId", "teamId", "x", "y");
        AssertJsonProperties(insight, "id", "matchId", "clockSeconds", "title", "summary", "classification", "detectorVersion", "eventIds", "frameIds");
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
    public void Domain_mapping_preserves_original_event_identity_sequence_and_occurrence_time()
    {
        var source = SyntheticFixtureGenerator.Generate();
        var mapped = source.ToResponse();

        Assert.Equal(source.Version, mapped.Version);
        Assert.NotEqual(ContractVersion.V1, mapped.Version);
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
