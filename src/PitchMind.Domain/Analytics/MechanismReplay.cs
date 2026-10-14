using System.Security.Cryptography;
using System.Text;
using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Analytics;

/// <summary>Read-only selected-time replay data for one simulated mechanism candidate.</summary>
public sealed record MechanismReplay(
    string Id,
    string MatchId,
    string SourceVersion,
    string SourceSha256,
    string Provenance,
    long? CanonicalRevision,
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
            insight.Provenance != "synthetic" || !IsSha256(graph.SourceSha256) ||
            graph.Provenance != "synthetic" || graph.Publishable || graph.PolicyStatus != "unapproved-demo" ||
            graph.MatchId != insight.MatchId || graph.SourceVersion != insight.SourceVersion || graph.SourceSha256 != insight.SourceSha256 ||
            graph.CanonicalRevision != insight.CanonicalRevision || graph.DetectorVersion != insight.DetectorVersion ||
            insight.EventIds.Count == 0 || insight.EventIds.Distinct().Count() != insight.EventIds.Count ||
            orderedEvents.Any(item => item.Event.MatchId.ToString() != insight.MatchId || item.Event.ClockSeconds > insight.SelectedTimeSeconds || item.Event.Sequence > insight.ThroughSequence) ||
            insight.EventIds.Any(id => orderedEvents.All(item => item.Event.EventId != id)) ||
            insight.FrameIds.Distinct(StringComparer.Ordinal).Count() != insight.FrameIds.Count ||
            orderedFrames.Any(frame => frame.ClockSeconds > insight.SelectedTimeSeconds) ||
            insight.FrameIds.Any(id => orderedFrames.All(frame => frame.Id != id)) ||
            graph.SelectedTimeSeconds != insight.SelectedTimeSeconds || graph.ThroughSequence != insight.ThroughSequence ||
            throughSequence != insight.ThroughSequence || string.IsNullOrWhiteSpace(evidence.CoordinateConvention))
            throw new InvalidDataException("Mechanism replay evidence must match the same synthetic source prefix and policy.");

        var sourceNodes = graph.Nodes.Where(node => node.Kind is "event" or "frame").ToArray();
        if (sourceNodes.Any(node => node.SourceVersion != graph.SourceVersion || node.SourceSha256 != graph.SourceSha256 ||
            node.CanonicalRevision != graph.CanonicalRevision))
            throw new InvalidDataException("Replay evidence nodes must retain the graph's source lineage.");

        var eventNodes = graph.Nodes.Where(node => node.Kind == "event").ToArray();
        var frameNodes = graph.Nodes.Where(node => node.Kind == "frame").ToArray();
        var graphEventIds = eventNodes.Select(node => node.EventId).Where(id => id.HasValue)
            .Select(id => id!.Value).Distinct().Order().ToArray();
        var replayEventIds = orderedEvents.Select(item => item.Event.EventId).Distinct().Order().ToArray();
        var graphFrameIds = frameNodes.Select(node => node.FrameId).Where(id => id is not null)
            .Select(id => id!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var replayFrameIds = orderedFrames.Select(frame => frame.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (eventNodes.Any(node => node.EventId is null) || frameNodes.Any(node => node.FrameId is null) ||
            eventNodes.Select(node => node.EventId).Distinct().Count() != eventNodes.Length ||
            frameNodes.Select(node => node.FrameId).Distinct(StringComparer.Ordinal).Count() != frameNodes.Length ||
            eventNodes.Any(node => node.ClockSeconds is null || node.Sequence is null) ||
            frameNodes.Any(node => node.ClockSeconds is null) ||
            orderedEvents.Select(item => item.Event.EventId).Distinct().Count() != orderedEvents.Length ||
            orderedFrames.Select(frame => frame.Id).Distinct(StringComparer.Ordinal).Count() != orderedFrames.Length ||
            orderedEvents.Select(item => item.Event.Sequence).Distinct().Count() != orderedEvents.Length ||
            orderedEvents.Zip(orderedEvents.Skip(1)).Any(pair => pair.First.Event.ClockSeconds > pair.Second.Event.ClockSeconds) ||
            !graphEventIds.SequenceEqual(replayEventIds) || !graphFrameIds.SequenceEqual(replayFrameIds) ||
            orderedEvents.Any(item => !eventNodes.Any(node => node.EventId == item.Event.EventId &&
                node.ClockSeconds == item.Event.ClockSeconds && node.Sequence == item.Event.Sequence)) ||
            orderedFrames.Any(frame => !frameNodes.Any(node => node.FrameId == frame.Id && node.ClockSeconds == frame.ClockSeconds)))
            throw new InvalidDataException("Replay source events and frames must resolve every graph source node exactly.");

        var times = orderedEvents.Select(item => item.Event.ClockSeconds)
            .Concat(orderedFrames.Select(frame => frame.ClockSeconds)).ToArray();
        var participants = orderedEvents.SelectMany(item => new[] { item.PlayerId, item.RelatedPlayerId ?? string.Empty })
            .Concat(orderedFrames.SelectMany(frame => frame.Players.Select(player => player.PlayerId)))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var eventIds = orderedEvents.Select(item => item.Event.EventId).ToArray();
        var frameIds = orderedFrames.Select(frame => frame.Id).ToArray();
        var stableInput = string.Join(':', insight.MatchId, insight.SourceVersion, graph.SourceSha256,
            insight.CanonicalRevision?.ToString() ?? "unrevisioned", insight.SelectedTimeSeconds, throughSequence, insight.DetectorVersion,
            string.Join(',', eventIds.Select(id => id.ToString("N"))), string.Join(',', frameIds));
        var replayId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableInput))).ToLowerInvariant();

        return new MechanismReplay(replayId, insight.MatchId, insight.SourceVersion, graph.SourceSha256, graph.Provenance,
            insight.CanonicalRevision, insight.SelectedTimeSeconds, throughSequence, times.Min(), times.Max(),
            insight.DetectorVersion, insight.PolicyStatus, false,
            Array.AsReadOnly(eventIds), Array.AsReadOnly(insight.EventIds.ToArray()), Array.AsReadOnly(frameIds), Array.AsReadOnly(participants),
            Array.AsReadOnly(orderedEvents), Array.AsReadOnly(orderedFrames), evidence.CoordinateConvention, graph,
            Array.AsReadOnly(evidence.Limitations.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static bool IsSha256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
