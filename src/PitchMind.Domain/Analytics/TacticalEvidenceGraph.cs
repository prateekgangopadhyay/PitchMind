using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Analytics;

public sealed record TacticalEvidenceNode(
    string Id,
    string Kind,
    string TruthClass,
    string Description,
    Guid? EventId,
    string? FrameId,
    int? ClockSeconds,
    long? Sequence,
    string SourceVersion,
    int? CanonicalRevision,
    decimal? NumericValue,
    string? Unit);

public sealed record TacticalEvidenceEdge(string FromNodeId, string ToNodeId, string Relation);

public sealed record TacticalEvidenceGraph(
    string MatchId,
    string SourceVersion,
    string Provenance,
    int? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    string DetectorVersion,
    string PolicyStatus,
    bool Publishable,
    string ClaimNodeId,
    IReadOnlyList<TacticalEvidenceNode> Nodes,
    IReadOnlyList<TacticalEvidenceEdge> Edges);

/// <summary>Builds source-linked, non-publishable evidence graphs for the deterministic demo detector.</summary>
public static class TacticalEvidenceGraphBuilder
{
    public static TacticalEvidenceGraph Build(TacticalInsight insight, IReadOnlyList<SimulationEvent> sourceEvents,
        IReadOnlyList<TrackingFrame> sourceFrames)
    {
        ArgumentNullException.ThrowIfNull(insight);
        ArgumentNullException.ThrowIfNull(sourceEvents);
        ArgumentNullException.ThrowIfNull(sourceFrames);

        var sequenceEvents = sourceEvents.Where(item => insight.EventIds.Contains(item.Event.EventId))
            .OrderBy(item => item.Event.Sequence).ToArray();
        var contextEvents = sourceEvents.Where(item => item.Event.ClockSeconds <= insight.SelectedTimeSeconds &&
            item.Event.Sequence <= insight.ThroughSequence && item.Event.MatchId.ToString() == insight.MatchId &&
            item.Event.Type is PitchMind.Domain.Events.MatchEventType.Goal or PitchMind.Domain.Events.MatchEventType.Substitution or
                PitchMind.Domain.Events.MatchEventType.RedCard or PitchMind.Domain.Events.MatchEventType.YellowCard or
                PitchMind.Domain.Events.MatchEventType.KickOff or PitchMind.Domain.Events.MatchEventType.HalfTime or
                PitchMind.Domain.Events.MatchEventType.FullTime).ToArray();
        var graphEvents = sequenceEvents.Concat(contextEvents).DistinctBy(item => item.Event.EventId)
            .OrderBy(item => item.Event.Sequence).ToArray();
        var selectedFrames = sourceFrames.Where(frame => insight.FrameIds.Contains(frame.Id))
            .OrderBy(frame => frame.ClockSeconds).ThenBy(frame => frame.Id, StringComparer.Ordinal).ToArray();
        if (sequenceEvents.Length != insight.EventIds.Count || selectedFrames.Length != insight.FrameIds.Count ||
            graphEvents.Any(item => item.Event.MatchId.ToString() != insight.MatchId ||
                item.Event.ClockSeconds > insight.SelectedTimeSeconds || item.Event.Sequence > insight.ThroughSequence) ||
            selectedFrames.Any(frame => frame.ClockSeconds > insight.SelectedTimeSeconds))
            throw new InvalidDataException("Every graph reference must resolve to source evidence inside the selected-time prefix.");

        var claimNodeId = $"claim:{insight.Id}";
        var nodes = new List<TacticalEvidenceNode>
        {
            new(claimNodeId, "claim", "pattern", insight.Summary, null, null, insight.ClockSeconds,
                insight.ThroughSequence, insight.SourceVersion, insight.CanonicalRevision, null, null)
        };
        nodes.AddRange(graphEvents.Select(item => new TacticalEvidenceNode(
            $"event:{item.Event.EventId:D}", "event", "fact",
            $"Synthetic source event: {item.Event.Type} ({item.Action}, {item.Outcome}).",
            item.Event.EventId, null, item.Event.ClockSeconds, item.Event.Sequence,
            insight.SourceVersion, insight.CanonicalRevision, null, null)));
        nodes.AddRange(selectedFrames.Select(frame => new TacticalEvidenceNode(
            $"frame:{frame.Id}", "frame", "fact", "Synthetic tracking frame used by the detector.",
            null, frame.Id, frame.ClockSeconds, null, insight.SourceVersion, insight.CanonicalRevision, null, null)));
        if (insight.Context is not null)
            nodes.Add(new($"metric:{insight.Id}:selected-context", "metric", "derivation",
                $"Selected-time scoreline {insight.Context.Scoreline}; pressing-team role {insight.Context.TeamRole}; " +
                $"active-player difference {insight.Context.NumericalAdvantage}; match phase {insight.Context.MatchPhase}; " +
                $"opponent phase {insight.Context.OpponentPhase}.", null, null, insight.SelectedTimeSeconds,
                insight.ThroughSequence, insight.SourceVersion, insight.CanonicalRevision,
                insight.Context.NumericalAdvantage, "active-players"));

        var edges = new List<TacticalEvidenceEdge>();
        for (var i = 1; i < graphEvents.Length; i++)
            edges.Add(new($"event:{graphEvents[i - 1].Event.EventId:D}",
                $"event:{graphEvents[i].Event.EventId:D}", "precedes"));
        edges.AddRange(graphEvents.Where(item => sequenceEvents.Any(sequence => sequence.Event.EventId == item.Event.EventId))
            .Select(item => new TacticalEvidenceEdge(claimNodeId,
            $"event:{item.Event.EventId:D}", "derived_from")));
        edges.AddRange(selectedFrames.Select(frame => new TacticalEvidenceEdge(claimNodeId,
            $"frame:{frame.Id}", "derived_from")));
        if (insight.Context is not null)
        {
            var metricId = $"metric:{insight.Id}:selected-context";
            edges.Add(new(claimNodeId, metricId, "derived_from"));
            edges.AddRange(contextEvents.Select(item => new TacticalEvidenceEdge(metricId,
                $"event:{item.Event.EventId:D}", "derived_from")));
        }

        var graph = new TacticalEvidenceGraph(insight.MatchId, insight.SourceVersion, "synthetic",
            insight.CanonicalRevision, insight.SelectedTimeSeconds, insight.ThroughSequence,
            insight.DetectorVersion, insight.PolicyStatus, insight.Publishable, claimNodeId,
            Array.AsReadOnly(nodes.ToArray()), Array.AsReadOnly(edges.ToArray()));
        TacticalEvidenceGraphValidator.Validate(graph);
        return graph;
    }
}

public static class TacticalEvidenceGraphValidator
{
    public static void Validate(TacticalEvidenceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (string.IsNullOrWhiteSpace(graph.MatchId) || string.IsNullOrWhiteSpace(graph.SourceVersion) ||
            graph.SelectedTimeSeconds < 0 || graph.CanonicalRevision is <= 0 || graph.Nodes is null || graph.Edges is null)
            throw new ArgumentException("Evidence graph metadata is invalid.", nameof(graph));

        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        if (nodes.Count == 0 || !nodes.TryGetValue(graph.ClaimNodeId, out var claim) ||
            claim.Kind != "claim" || claim.TruthClass != "pattern")
            throw new InvalidDataException("The graph must have one identified pattern claim node.");
        if (graph.Nodes.Any(node => string.IsNullOrWhiteSpace(node.Id) || node.SourceVersion != graph.SourceVersion ||
            node.CanonicalRevision != graph.CanonicalRevision || node.ClockSeconds is int clock &&
            (clock < 0 || clock > graph.SelectedTimeSeconds) ||
            node.Kind == "claim" && node.TruthClass != "pattern" ||
            (node.Kind is "event" or "frame") && node.TruthClass != "fact" ||
            node.Kind == "metric" && node.TruthClass != "derivation" ||
            node.Kind == "metric" && (node.NumericValue is null || string.IsNullOrWhiteSpace(node.Unit)) ||
            node.Kind is not "metric" && (node.NumericValue is not null || node.Unit is not null) ||
            node.Kind is not ("claim" or "event" or "frame" or "metric") ||
            node.Kind == "event" && (node.EventId is null || node.Sequence is null || node.ClockSeconds is null || node.FrameId is not null) ||
            node.Kind == "frame" && (node.FrameId is null || node.ClockSeconds is null || node.EventId is not null || node.Sequence is not null) ||
            (node.Kind is "claim" or "metric") && (node.EventId is not null || node.FrameId is not null)))
            throw new InvalidDataException("Evidence nodes must use the graph's source revision and selected time.");

        foreach (var edge in graph.Edges)
        {
            if (!nodes.ContainsKey(edge.FromNodeId) || !nodes.ContainsKey(edge.ToNodeId))
                throw new InvalidDataException("Evidence graph edges must resolve both endpoints.");
            if (edge.Relation == "derived_from" &&
                (nodes[edge.FromNodeId].Kind is not ("claim" or "metric") ||
                 nodes[edge.ToNodeId].Kind is not ("event" or "frame" or "metric") || edge.FromNodeId == edge.ToNodeId))
                throw new InvalidDataException("A derived_from edge must connect a claim or derivation to source evidence.");
            if (edge.Relation == "precedes" &&
                (nodes[edge.FromNodeId].Kind != "event" || nodes[edge.ToNodeId].Kind != "event" ||
                 nodes[edge.FromNodeId].Sequence is not long from || nodes[edge.ToNodeId].Sequence is not long to || to <= from))
                throw new InvalidDataException("A precedes edge must preserve source event sequence.");
            if (edge.Relation is not ("derived_from" or "precedes"))
                throw new InvalidDataException("Unsupported evidence edge relation.");
        }

        if (graph.Publishable)
        {
            var claimHasSourceEvidence = graph.Edges.Any(edge => edge.FromNodeId == graph.ClaimNodeId &&
                edge.Relation == "derived_from" && (nodes[edge.ToNodeId].Kind is "event" or "frame")) ||
                graph.Edges.Any(claimEdge => claimEdge.FromNodeId == graph.ClaimNodeId &&
                    claimEdge.Relation == "derived_from" && nodes[claimEdge.ToNodeId].Kind == "metric" &&
                    graph.Edges.Any(metricEdge => metricEdge.FromNodeId == claimEdge.ToNodeId &&
                        metricEdge.Relation == "derived_from" && (nodes[metricEdge.ToNodeId].Kind is "event" or "frame")));
            if (graph.Provenance != "canonical" || graph.CanonicalRevision is null ||
                graph.PolicyStatus != "approved" || !claimHasSourceEvidence)
                throw new InvalidDataException("A publishable claim requires approved policy and revisioned canonical source evidence.");
        }
    }
}
