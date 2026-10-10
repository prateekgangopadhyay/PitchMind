using System.Security.Cryptography;
using System.Text;
using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Analytics;

/// <summary>Read-only selected-time replay data for one simulated mechanism candidate.</summary>
public sealed record MechanismReplay(
    string Id,
    string MatchId,
    string SourceVersion,
    string Provenance,
    int? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    int StartSeconds,
    int EndSeconds,
    string DetectorVersion,
    string PolicyStatus,
    bool Publishable,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<Guid> MechanismEventIds,
    IReadOnlyList<string> FrameIds,
    IReadOnlyList<string> ParticipantIds,
    IReadOnlyList<SimulationEvent> Events,
    IReadOnlyList<TrackingFrame> Frames,
    string CoordinateConvention,
    TacticalEvidenceGraph EvidenceGraph,
    IReadOnlyList<string> Limitations);

public static class MechanismReplayBuilder
{
    public static MechanismReplay Build(TacticalEvidence evidence, long throughSequence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var insight = evidence.Insight;
        var orderedEvents = evidence.Events.OrderBy(item => item.Event.Sequence).ToArray();
        var orderedFrames = evidence.Frames.OrderBy(frame => frame.ClockSeconds).ThenBy(frame => frame.Id, StringComparer.Ordinal).ToArray();
        var graph = insight.EvidenceGraph ?? throw new InvalidDataException("Mechanism replay requires a validated evidence graph.");

        if (insight.Classification != "simulated" || insight.Publishable || insight.PolicyStatus != "unapproved-demo" ||
            graph.Provenance != "synthetic" || graph.Publishable || graph.PolicyStatus != "unapproved-demo" ||
            graph.MatchId != insight.MatchId || graph.SourceVersion != insight.SourceVersion ||
            graph.CanonicalRevision != insight.CanonicalRevision || graph.DetectorVersion != insight.DetectorVersion ||
            insight.EventIds.Count == 0 || insight.EventIds.Distinct().Count() != insight.EventIds.Count ||
            orderedEvents.Any(item => item.Event.MatchId.ToString() != insight.MatchId || item.Event.ClockSeconds > insight.SelectedTimeSeconds || item.Event.Sequence > insight.ThroughSequence) ||
            insight.EventIds.Any(id => orderedEvents.All(item => item.Event.EventId != id)) ||
            insight.FrameIds.Distinct(StringComparer.Ordinal).Count() != insight.FrameIds.Count ||
            orderedFrames.Any(frame => frame.ClockSeconds > insight.SelectedTimeSeconds) ||
            insight.FrameIds.Any(id => orderedFrames.All(frame => frame.Id != id)) ||
            graph.SelectedTimeSeconds != insight.SelectedTimeSeconds || graph.ThroughSequence != insight.ThroughSequence ||
            throughSequence != insight.ThroughSequence)
            throw new InvalidDataException("Mechanism replay evidence must match the same synthetic source prefix and policy.");

        var graphEventIds = graph.Nodes.Where(node => node.Kind == "event").Select(node => node.EventId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order().ToArray();
        var replayEventIds = orderedEvents.Select(item => item.Event.EventId).Distinct().Order().ToArray();
        var graphFrameIds = graph.Nodes.Where(node => node.Kind == "frame").Select(node => node.FrameId)
            .Where(id => id is not null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var replayFrameIds = orderedFrames.Select(frame => frame.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (graph.Nodes.Where(node => node.Kind == "event").Any(node => node.EventId is null) ||
            graph.Nodes.Where(node => node.Kind == "frame").Any(node => node.FrameId is null) ||
            !graphEventIds.SequenceEqual(replayEventIds) || !graphFrameIds.SequenceEqual(replayFrameIds))
            throw new InvalidDataException("Replay source events and frames must resolve every graph source node exactly.");

        var times = orderedEvents.Select(item => item.Event.ClockSeconds)
            .Concat(orderedFrames.Select(frame => frame.ClockSeconds)).ToArray();
        var participants = orderedEvents.SelectMany(item => new[] { item.PlayerId, item.RelatedPlayerId ?? string.Empty })
            .Concat(orderedFrames.SelectMany(frame => frame.Players.Select(player => player.PlayerId)))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var eventIds = orderedEvents.Select(item => item.Event.EventId).ToArray();
        var frameIds = orderedFrames.Select(frame => frame.Id).ToArray();
        var stableInput = string.Join(':', insight.MatchId, insight.SourceVersion, insight.DetectorVersion,
            string.Join(',', eventIds.Select(id => id.ToString("N"))), string.Join(',', frameIds));
        var replayId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableInput))).ToLowerInvariant();

        return new MechanismReplay(replayId, insight.MatchId, insight.SourceVersion, graph.Provenance,
            insight.CanonicalRevision, insight.SelectedTimeSeconds, throughSequence, times.Min(), times.Max(),
            insight.DetectorVersion, insight.PolicyStatus, false,
            Array.AsReadOnly(eventIds), Array.AsReadOnly(insight.EventIds.ToArray()), Array.AsReadOnly(frameIds), Array.AsReadOnly(participants),
            Array.AsReadOnly(orderedEvents), Array.AsReadOnly(orderedFrames), evidence.CoordinateConvention, graph,
            Array.AsReadOnly(evidence.Limitations.Distinct(StringComparer.Ordinal).ToArray()));
    }
}
