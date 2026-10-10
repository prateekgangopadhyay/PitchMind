using PitchMind.Domain.Analytics;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class PressSequenceDetectorTests
{
    private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
            10, 1, "synthetic-v2", SourceHash, null, null, null);
        var source = new TacticalEvidenceNode("event:one", "event", "fact", "source", Guid.NewGuid(), null,
            9, 1, "synthetic-v2", SourceHash, null, null, null);
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", SourceHash, "synthetic", null, 10, 1,
            "press-sequence-v2", "unapproved-demo", true, claim.Id,
            new[] { claim, source }, new[] { new TacticalEvidenceEdge(claim.Id, source.Id, "derived_from") },
            "Uncalibrated.", new[] { new TacticalEvidenceCondition("test", true, "observed", null, new[] { source.Id }, null) });

        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));
    }

    [Theory]
    [InlineData("fact")]
    [InlineData("derivation")]
    [InlineData("pattern")]
    [InlineData("interpretation")]
    [InlineData("hypothesis")]
    public void PreservesSupportedClaimTruthClasses(string truthClass)
    {
        var claim = new TacticalEvidenceNode("claim:one", "claim", truthClass, "claim", null, null,
            10, 1, "synthetic-v2", SourceHash, null, null, null);
        var graph = new TacticalEvidenceGraph("match", "synthetic-v2", SourceHash, "synthetic", null, 10, 1,
            "press-sequence-v2", "unapproved-demo", false, claim.Id, new[] { claim }, Array.Empty<TacticalEvidenceEdge>(),
            "Uncalibrated.", new[] { new TacticalEvidenceCondition("test", true, "observed", null, Array.Empty<string>(), null) });

        TacticalEvidenceGraphValidator.Validate(graph);
    }

    [Fact]
    public void RejectsUnsupportedTruthClassAndSourceHashMismatch()
    {
        var unsupported = new TacticalEvidenceNode("claim:one", "claim", "unsupported", "claim", null, null,
            10, 1, "synthetic-v2", SourceHash, null, null, null);
        var unsupportedGraph = new TacticalEvidenceGraph("match", "synthetic-v2", SourceHash, "synthetic", null,
            10, 1, "press-sequence-v2", "unapproved-demo", false, unsupported.Id,
            new[] { unsupported }, Array.Empty<TacticalEvidenceEdge>(), "Uncalibrated.",
            new[] { new TacticalEvidenceCondition("test", true, "observed", null, Array.Empty<string>(), null) });
        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(unsupportedGraph));

        var mismatchedNode = unsupported with { TruthClass = "pattern", SourceSha256 = new string('b', 64) };
        var mismatchedGraph = unsupportedGraph with { Nodes = new[] { mismatchedNode } };
        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(mismatchedGraph));
    }

    [Fact]
    public void HypothesisCannotBePublishedAsSupportedClaim()
    {
        var claim = new TacticalEvidenceNode("claim:one", "claim", "hypothesis", "candidate", null, null,
            10, 1, "source-v1", SourceHash, 1, null, null);
        var source = new TacticalEvidenceNode("event:one", "event", "fact", "observed source", Guid.NewGuid(), null,
            9, 1, "source-v1", SourceHash, 1, null, null);
        var graph = new TacticalEvidenceGraph("match", "source-v1", SourceHash, "canonical", 1, 10, 1,
            "detector-v1", "approved", true, claim.Id, new[] { claim, source },
            new[] { new TacticalEvidenceEdge(claim.Id, source.Id, "derived_from") }, "No calibrated confidence score.",
            new[] { new TacticalEvidenceCondition("evidence", true, "source observed", null, new[] { source.Id }, null) });

        var error = Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(graph));

        Assert.Equal("A hypothesis cannot be published as a supported tactical claim.", error.Message);
    }

    [Fact]
    public void FailedConditionMustNameAnExclusionAndResolveItsEvidence()
    {
        var claim = new TacticalEvidenceNode("claim:one", "claim", "pattern", "candidate", null, null,
            10, 1, "source-v1", SourceHash, null, null, null);
        var eventNode = new TacticalEvidenceNode("event:one", "event", "fact", "observed source", Guid.NewGuid(), null,
            9, 1, "source-v1", SourceHash, null, null, null);
        var condition = new TacticalEvidenceCondition("tracking-coverage", false, "required frame missing", null,
            new[] { eventNode.Id }, "No frame contains the required pressure participants.");
        var graph = new TacticalEvidenceGraph("match", "source-v1", SourceHash, "synthetic", null, 10, 1,
            "detector-v1", "unapproved-demo", false, claim.Id, new[] { claim, eventNode },
            Array.Empty<TacticalEvidenceEdge>(), "Insufficient tracking; no confidence score.", new[] { condition });

        TacticalEvidenceGraphValidator.Validate(graph);

        var dangling = graph with { Conditions = new[] { condition with { SourceNodeIds = new[] { "frame:missing" } } } };
        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(dangling));
        var unexplained = graph with { Conditions = new[] { condition with { ExclusionReason = null } } };
        Assert.Throws<InvalidDataException>(() => TacticalEvidenceGraphValidator.Validate(unexplained));
    }
}
