using PitchMind.Domain.Analytics;
using PitchMind.Domain.Simulation;

namespace PitchMind.Api;

using PitchMind.Contracts.V1;

/// <summary>Maps internal read models to the stable v1 wire representation.</summary>
public static class MatchContractMappings
{
    public static FixtureResponse ToResponse(this SyntheticFixture fixture) => new(
        fixture.MatchId,
        fixture.Version,
        fixture.Seed,
        fixture.Provenance,
        fixture.DurationSeconds,
        fixture.HalfTimeSeconds,
        fixture.PitchLengthMetres,
        fixture.PitchWidthMetres,
        fixture.Home.ToResponse(),
        fixture.Away.ToResponse(),
        Array.AsReadOnly(fixture.Events.Select(ToResponse).ToArray()),
        Array.AsReadOnly(fixture.Frames.Select(frame => frame.ToResponse(fixture.CoordinateConvention)).ToArray()));

    public static MatchStateResponse ToResponse(this MatchSnapshot snapshot) => new(
        snapshot.MatchId,
        snapshot.ClockSeconds,
        snapshot.Period,
        snapshot.Status,
        snapshot.Phase.ToString().ToLowerInvariant(),
        snapshot.HomeScore,
        snapshot.AwayScore,
        snapshot.Provenance,
        snapshot.Frame.ToResponse(snapshot.CoordinateConvention),
        snapshot.LastSequence,
        snapshot.PossessionTeamId,
        snapshot.HomeActivePlayers,
        snapshot.AwayActivePlayers,
        Array.AsReadOnly(snapshot.Players.Select(player => new MatchPlayerStateResponse(
            player.PlayerId, player.TeamId, player.Active, player.Dismissed, player.YellowCards)).ToArray()));

    public static InsightResponse ToResponse(this TacticalInsight insight) => new(
        insight.Id,
        insight.MatchId,
        insight.ClockSeconds,
        insight.Title,
        insight.Summary,
        insight.Classification,
        insight.DetectorVersion,
        Array.AsReadOnly(insight.EventIds.ToArray()),
        Array.AsReadOnly(insight.FrameIds.ToArray()),
        insight.SourceVersion,
        insight.CanonicalRevision,
        insight.SelectedTimeSeconds,
        insight.ThroughSequence,
        insight.PolicyStatus,
        insight.Publishable,
        Array.AsReadOnly(insight.Conditions.ToArray()),
        insight.Context is null ? null : new TacticalContextResponse(
            insight.Context.Scoreline, insight.Context.TeamRole, insight.Context.NumericalAdvantage,
            insight.Context.OpponentPhase, insight.Context.MatchPhase),
        insight.EvidenceGraph is null ? null : Map(insight.EvidenceGraph));

    private static EvidenceGraphResponse Map(TacticalEvidenceGraph graph) => new(
        graph.MatchId, graph.SourceVersion, graph.Provenance, graph.CanonicalRevision,
        graph.SelectedTimeSeconds, graph.ThroughSequence, graph.DetectorVersion,
        graph.PolicyStatus, graph.Publishable, graph.ClaimNodeId,
        Array.AsReadOnly(graph.Nodes.Select(node => new EvidenceNodeResponse(
            node.Id, node.Kind, node.TruthClass, node.Description, node.EventId, node.FrameId,
            node.ClockSeconds, node.Sequence, node.SourceVersion, node.CanonicalRevision, node.NumericValue, node.Unit)).ToArray()),
        Array.AsReadOnly(graph.Edges.Select(edge => new EvidenceEdgeResponse(
            edge.FromNodeId, edge.ToNodeId, edge.Relation)).ToArray()));

    public static EvidenceResponse ToResponse(this TacticalEvidence evidence) => new(
        evidence.Insight.ToResponse(),
        Array.AsReadOnly(evidence.Events.Select(ToResponse).ToArray()),
        Array.AsReadOnly(evidence.Frames.Select(frame => frame.ToResponse(evidence.CoordinateConvention)).ToArray()),
        evidence.CoordinateConvention,
        Array.AsReadOnly(evidence.Conditions.ToArray()),
        Array.AsReadOnly(evidence.Limitations.ToArray()));

    public static ExplanationResponse ToResponse(this MatchExplanation explanation) => new(
        explanation.InsightId,
        explanation.ClockSeconds,
        explanation.Audience,
        explanation.Source,
        explanation.Text,
        Array.AsReadOnly(explanation.EventIds.ToArray()),
        Array.AsReadOnly(explanation.Limitations.ToArray()));

    private static TeamResponse ToResponse(this SyntheticTeam team) => new(
        team.Id, team.Name, team.Formation,
        Array.AsReadOnly(team.Players.Select(player => new PlayerResponse(player.Id, player.Name, player.TeamId, player.ShirtNumber, player.Role)).ToArray()));

    private static TrackingFrameResponse ToResponse(this TrackingFrame frame, string coordinateConvention) => new(
        frame.Id, frame.ClockSeconds, frame.BallX, frame.BallY,
        coordinateConvention,
        Array.AsReadOnly(frame.Players.Select(player => new PitchPositionResponse(player.PlayerId, player.TeamId, player.X, player.Y)).ToArray()));

    public static SimulationEventResponse ToResponse(this SimulationEvent simulationEvent) => new(
        new MatchEventResponse(
            simulationEvent.Event.EventId,
            simulationEvent.Event.MatchId.ToString(),
            simulationEvent.Event.Sequence,
            simulationEvent.Event.Type.ToString(),
            simulationEvent.Event.ClockSeconds,
            simulationEvent.Event.Period,
            simulationEvent.Event.OccurredAt),
        simulationEvent.TeamId,
        simulationEvent.PlayerId,
        simulationEvent.Action,
        simulationEvent.Outcome,
        simulationEvent.RelatedPlayerId);
}
