using System.Text;
using System.Text.Json;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CandidateAnalystEvidenceProjectionTests
{
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
        var outerRequest = JsonSerializer.Serialize(new
        {
            input = innerRequest,
            max_output_tokens = 512,
            text = new
            {
                format = new
                {
                    type = "json_schema", name = "candidate_match_answer", strict = true,
                    schema = new
                    {
                        type = "object", additionalProperties = false,
                        required = new[] { "status", "answer", "claims" },
                        properties = new
                        {
                            status = new { type = "string", @enum = new[] { "answered", "insufficientEvidence" } },
                            answer = new { type = "string" },
                            claims = new
                            {
                                type = "array", items = new
                                {
                                    type = "object", additionalProperties = false,
                                    required = new[] { "text", "evidenceIds" },
                                    properties = new
                                    {
                                        text = new { type = "string" },
                                        evidenceIds = new { type = "array", items = new { type = "string" } }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
        Assert.True(Encoding.UTF8.GetByteCount(innerRequest) < 32_768);
        Assert.True(Encoding.UTF8.GetByteCount(outerRequest) < 40_960,
            $"Serialized Foundry request was {Encoding.UTF8.GetByteCount(outerRequest)} bytes.");
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
