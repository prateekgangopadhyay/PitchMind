using PitchMind.Domain.Analytics;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class PressSequenceDetectorTests
{
    [Fact]
    public void RejectsNonSyntheticProvenance()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        var input = new PressSequenceInput(
            fixture.MatchId,
            fixture.Version,
            "live-provider",
            null,
            5400,
            events[^1].Event.Sequence,
            fixture.Home.Id,
            fixture.Home.Name,
            fixture.Away.Id,
            fixture.Away.Name,
            "stadium-metres",
            fixture.PitchLengthMetres,
            fixture.PitchWidthMetres,
            events,
            fixture.Frames)
        {
            SelectedState = new MatchState(fixture.MatchId, 5400, 2, MatchPhase.SecondHalf,
                0, 0, null, 11, 11, Array.Empty<MatchPlayerState>(), events[^1].Event.Sequence, "live-provider")
        };

        Assert.Throws<ArgumentException>(() => PressSequenceDetector.Detect(input));
    }

    [Fact]
    public void RejectsPublishingGraphWithoutApprovedRevisionedCanonicalEvidence()
    {
        var claim = new TacticalEvidenceNode("claim:one", "claim", "pattern", "claim", null, null,
            10, 1, "synthetic-v2", null, null, null);
        var source = new TacticalEvidenceNode("event:one", "event", "fact", "source", Guid.NewGuid(), null,
            9, 1, "synthetic-v2", null, null, null);
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", "synthetic", null, 10, 1,
            "press-sequence-v2", "unapproved-demo", true, claim.Id,
            new[] { claim, source }, new[] { new TacticalEvidenceEdge(claim.Id, source.Id, "derived_from") });

        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));
    }
}
