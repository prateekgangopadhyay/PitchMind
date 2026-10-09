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
}
