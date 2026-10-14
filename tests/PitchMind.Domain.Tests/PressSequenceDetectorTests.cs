using PitchMind.Domain.Analytics;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class PressSequenceDetectorTests
{
    [Fact]
    public void RejectsSkippedSequenceInClaimedCompletePrefix()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var completePrefix = fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        var skippedPrefix = completePrefix.Where((_, index) => index != 1).ToArray();
        var input = new PressSequenceInput(
            fixture.MatchId,
            fixture.Version,
            fixture.Provenance,
            null,
            5400,
            completePrefix[^1].Event.Sequence,
            fixture.Home.Id,
            fixture.Home.Name,
            fixture.Away.Id,
            fixture.Away.Name,
            "stadium-metres",
            fixture.PitchLengthMetres,
            fixture.PitchWidthMetres,
            skippedPrefix,
            fixture.Frames);

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(input));
    }

    [Fact]
    public void SuppressesPressureDistanceWhenSameTimeTrackingIsAmbiguous()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var pressure = fixture.Events.Single(item => item.Action == "press-arrival");
        var pressureFrame = fixture.Frames.Single(frame => frame.ClockSeconds == pressure.Event.ClockSeconds);
        var frames = fixture.Frames.Append(pressureFrame with { Id = pressureFrame.Id + "-duplicate" }).ToArray();

        Assert.Empty(PressSequenceDetector.Detect(Input(fixture, frames)));
    }

    [Fact]
    public void RejectsDuplicateSourceEventIds()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events.ToArray();
        events[^1] = events[^1] with
        {
            Event = events[^1].Event with { EventId = events[0].Event.EventId }
        };

        Assert.Throws<InvalidDataException>(() => PressSequenceDetector.Detect(Input(fixture, events: events)));
    }

    private static PressSequenceInput Input(SyntheticFixture fixture,
        IReadOnlyList<TrackingFrame>? frames = null, IReadOnlyList<SimulationEvent>? events = null)
    {
        var selectedEvents = events ?? fixture.Events.Where(item => item.Event.ClockSeconds <= 5400).ToArray();
        return new PressSequenceInput(fixture.MatchId, fixture.Version, fixture.Provenance, null, 5400,
            selectedEvents.Count == 0 ? -1 : selectedEvents[^1].Event.Sequence,
            fixture.Home.Id, fixture.Home.Name, fixture.Away.Id, fixture.Away.Name, "stadium-metres",
            fixture.PitchLengthMetres, fixture.PitchWidthMetres, selectedEvents, frames ?? fixture.Frames);
    }

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
            fixture.Frames);

        Assert.Throws<ArgumentException>(() => PressSequenceDetector.Detect(input));
    }
}
