using System.Text;
using System.Text.Json;
using PitchMind.Api;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CandidateAnalystEvidenceProjectionTests
{
    [Fact]
    public void Agent_summary_is_replaced_by_validated_claims_and_duplicate_evidence_ids_are_removed()
    {
        var claims = new CandidateAnalystClaim[]
        {
            new("The shot was saved.", ["event-121"]),
            new("Pressure preceded the shot.", ["event-120", "event-120", "event-119"])
        };

        var normalized = PitchMind.Api.CandidateMatchAnalystService.NormalizeGeneratedAnswer(
            "answered", "Uncited extra narrative from the agent.", claims,
            new HashSet<string>(["event-119", "event-120", "event-121"], StringComparer.Ordinal));

        Assert.Equal("The shot was saved. Pressure preceded the shot.", normalized.Answer);
        Assert.Equal(["event-120", "event-119"], normalized.Claims[1].EvidenceIds);
    }

    [Fact]
    public void Agent_claim_with_evidence_outside_the_selected_packet_is_rejected()
    {
        var claims = new CandidateAnalystClaim[]
        {
            new("Unsupported event claim.", ["event-from-another-match"])
        };

        Assert.Throws<PitchMind.Api.CandidateAnalystUnavailableException>(() =>
            PitchMind.Api.CandidateMatchAnalystService.NormalizeGeneratedAnswer(
                "answered", "Unsupported event claim.", claims,
                new HashSet<string>(["event-121"], StringComparer.Ordinal)));
    }

    [Fact]
    public void Large_source_event_is_reduced_to_bounded_evidence_without_dropping_identity_or_time()
    {
        var source = CreateSourceEvent();
        var rawPayload = JsonSerializer.SerializeToElement(source);
        Assert.True(Encoding.UTF8.GetByteCount(rawPayload.GetRawText()) > 6_144);

        var projected = PitchMind.Api.CandidateMatchAnalystService.ProjectEvidencePayload(rawPayload);
        var eventOnly = PitchMind.Api.CandidateMatchAnalystService.ProjectEvidencePayload(rawPayload,
            includePlayerPositions: false);
        var projectedBytes = Encoding.UTF8.GetByteCount(projected.GetRawText());

        Assert.InRange(projectedBytes, 1, 4_096);
        Assert.Equal("source-event-818", projected.GetProperty("eventId").GetString());
        Assert.Equal(818, projected.GetProperty("sequence").GetInt32());
        Assert.Equal(2_137, projected.GetProperty("clockSeconds").GetInt32());
        Assert.Equal(22, projected.GetProperty("playerPositions").GetArrayLength());
        Assert.False(projected.GetProperty("playerPositions")[0].TryGetProperty("heartRate", out _));
        Assert.False(projected.TryGetProperty("metadata", out _));
        Assert.False(eventOnly.TryGetProperty("playerPositions", out _));

        // The service sends compact event context plus one latest positional frame.
        var innerRequest = JsonSerializer.Serialize(new
        {
            records = Enumerable.Range(0, 8).Select(index => new
            {
                evidenceId = $"source-event-{index}", sequence = index, sourceClockValue = 2_137,
                sourcePeriod = 1, sourceEventType = "Tackle", rawPayload = index == 7 ? projected : eventOnly
            })
        });
        var outerRequest = PitchMind.Api.FoundryCandidateMatchAnswerGenerator.CreateResponseRequest(innerRequest);
        using var requestDocument = JsonDocument.Parse(outerRequest);
        var requestRoot = requestDocument.RootElement;
        Assert.True(Encoding.UTF8.GetByteCount(innerRequest) < 32_768);
        Assert.True(outerRequest.Length < 40_960,
            $"Serialized Foundry request was {outerRequest.Length} bytes.");
        Assert.Equal(2, requestRoot.EnumerateObject().Count());
        Assert.Equal(1_536, requestRoot.GetProperty("max_output_tokens").GetInt32());
        var agentInput = requestRoot.GetProperty("input");
        Assert.Equal(2, agentInput.GetArrayLength());
        Assert.Equal("message", agentInput[0].GetProperty("type").GetString());
        Assert.Equal("developer", agentInput[0].GetProperty("role").GetString());
        Assert.Contains("RESPONSE CONTRACT", agentInput[0].GetProperty("content")[0]
            .GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("at most 3 short sentences", agentInput[0].GetProperty("content")[0]
            .GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("message", agentInput[1].GetProperty("type").GetString());
        Assert.Equal("user", agentInput[1].GetProperty("role").GetString());
        Assert.Contains("MATCH EVIDENCE PACKET (JSON)", agentInput[1].GetProperty("content")[0]
            .GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("source-event-818", agentInput[1].GetProperty("content")[0]
            .GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.False(requestRoot.TryGetProperty("text", out _));
    }

    private static object CreateSourceEvent() => new
    {
        eventId = "source-event-818",
        matchId = "candidate-match",
        sequence = 818,
        clockSeconds = 2_137,
        period = 1,
        eventType = "Tackle",
        teamId = "team-a",
        teamName = "Team A",
        playerName = "Player 8",
        pitchX = 51.2,
        pitchY = 42.1,
        outcome = "Successful",
        metadata = new { providerNote = new string('x', 5_000) },
        playerPositions = Enumerable.Range(1, 22).Select(number => new
        {
            playerName = $"Player {number}",
            teamName = number <= 11 ? "Team A" : "Team B",
            teamId = number <= 11 ? "team-a" : "team-b",
            jerseyNumber = number,
            position = "CM",
            x = number * 2.5,
            y = number * 3.5,
            speed = 33.0,
            distanceCovered = 12_345.6,
            heatIndex = 0.98,
            passAccuracy = 80.0,
            touches = 40,
            stamina = 95.0,
            heartRate = 181
        }).ToArray()
    };
}
