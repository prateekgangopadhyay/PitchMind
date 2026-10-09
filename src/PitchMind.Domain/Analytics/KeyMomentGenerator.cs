using System.Security.Cryptography;
using System.Text;
using PitchMind.Domain.Events;
using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Analytics;

/// <summary>Unapproved deterministic demo relevance weights; callers may supply a reviewed policy.</summary>
public sealed record KeyMomentPolicy(
    string Version,
    decimal GoalWeight,
    decimal RedCardWeight,
    decimal YellowCardWeight,
    decimal ShotWeight,
    decimal TacticalPatternWeight,
    double RecencyHalfLifeSeconds,
    int ShotDeduplicationWindowSeconds,
    decimal MinimumRelevance,
    int MaximumMoments)
{
    public static KeyMomentPolicy UnapprovedDemoV1 { get; } = new(
        "key-moment-v1-demo", 100m, 75m, 50m, 30m, 60m, 900, 45, 1m, 20);

    public bool IsValid => !string.IsNullOrWhiteSpace(Version) &&
        GoalWeight >= 0 && RedCardWeight >= 0 && YellowCardWeight >= 0 && ShotWeight >= 0 && TacticalPatternWeight >= 0 &&
        double.IsFinite(RecencyHalfLifeSeconds) && RecencyHalfLifeSeconds > 0 &&
        ShotDeduplicationWindowSeconds is > 0 and <= 3600 &&
        MinimumRelevance >= 0 && MaximumMoments is > 0 and <= 100;
}

/// <summary>A selected-time, evidence-linked candidate for fan-facing relevance ordering.</summary>
public sealed record KeyMoment(
    string Id,
    string MatchId,
    string SourceVersion,
    string Provenance,
    int? CanonicalRevision,
    int SelectedTimeSeconds,
    long ThroughSequence,
    int ClockSeconds,
    long Sequence,
    string Kind,
    string TruthClass,
    string Title,
    string Summary,
    decimal Relevance,
    string PolicyVersion,
    string PolicyStatus,
    bool Publishable,
    IReadOnlyList<Guid> EventIds,
    IReadOnlyList<string> FrameIds,
    IReadOnlyList<string> EvidenceClaimNodeIds);

public static class KeyMomentGenerator
{
    public static IReadOnlyList<KeyMoment> Generate(
        string matchId,
        string sourceVersion,
        string provenance,
        int? canonicalRevision,
        int selectedTimeSeconds,
        long throughSequence,
        string homeTeamId,
        string homeTeamName,
        string awayTeamId,
        string awayTeamName,
        IReadOnlyList<SimulationEvent> events,
        IReadOnlyList<TacticalInsight> insights,
        KeyMomentPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(insights);
        policy ??= KeyMomentPolicy.UnapprovedDemoV1;
        if (!policy.IsValid) throw new ArgumentException("Key moment policy is invalid.", nameof(policy));
        if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(sourceVersion) ||
            provenance != "synthetic" || selectedTimeSeconds < 0 || throughSequence < -1 ||
            string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId) || homeTeamId == awayTeamId ||
            string.IsNullOrWhiteSpace(homeTeamName) || string.IsNullOrWhiteSpace(awayTeamName))
            throw new ArgumentException("Selected-time key moment source metadata is invalid.");
        if (events.Any(item => item.Event.MatchId.ToString() != matchId || item.Event.Sequence < 0 || item.Event.ClockSeconds < 0) ||
            events.Select(item => item.Event.EventId).Distinct().Count() != events.Count)
            throw new ArgumentException("Key moment source events must have unique IDs and valid match time/identity.", nameof(events));

        var orderedEvents = events.OrderBy(item => item.Event.Sequence).ToArray();
        if (orderedEvents.Zip(orderedEvents.Skip(1)).Any(pair => pair.First.Event.ClockSeconds > pair.Second.Event.ClockSeconds))
            throw new ArgumentException("Source event sequence and match time must be ordered.", nameof(events));
        var atSelectedTime = orderedEvents.Where(item => item.Event.ClockSeconds <= selectedTimeSeconds).ToArray();
        var expectedWatermark = atSelectedTime.Length == 0 ? -1 : atSelectedTime[^1].Event.Sequence;
        if (throughSequence != expectedWatermark)
            throw new ArgumentException("Declared sequence watermark must match the selected-time source prefix.", nameof(throughSequence));
        var prefix = atSelectedTime.Where(item => item.Event.Sequence <= throughSequence).ToArray();
        var prefixIds = prefix.Select(item => item.Event.EventId).ToHashSet();
        if (prefix.Select(item => item.Event.Sequence).Distinct().Count() != prefix.Length)
            throw new ArgumentException("Selected-time source sequence must be unique.", nameof(events));

        var candidates = new List<KeyMoment>();
        foreach (var item in prefix)
        {
            if (item.Event.Type == MatchEventType.Shot) continue;
            var (kind, weight, title, summary) = EventNarrative(item, homeTeamId, homeTeamName, awayTeamId, awayTeamName, policy);
            if (kind is null) continue;
            candidates.Add(Create(matchId, sourceVersion, provenance, canonicalRevision, selectedTimeSeconds, throughSequence,
                item.Event.ClockSeconds, item.Event.Sequence, kind, "fact", title, summary, weight,
                policy, [item.Event.EventId], [], []));
        }

        foreach (var teamGroup in prefix.Where(item => item.Event.Type == MatchEventType.Shot)
                     .GroupBy(item => item.TeamId, StringComparer.Ordinal))
        {
            var cluster = new List<SimulationEvent>();
            foreach (var item in teamGroup.OrderBy(item => item.Event.ClockSeconds).ThenBy(item => item.Event.Sequence))
            {
                if (cluster.Count > 0 && (item.Event.Period != cluster[^1].Event.Period ||
                    item.Event.ClockSeconds - cluster[^1].Event.ClockSeconds > policy.ShotDeduplicationWindowSeconds))
                {
                    AddShotCluster(cluster);
                    cluster.Clear();
                }
                cluster.Add(item);
            }
            if (cluster.Count > 0) AddShotCluster(cluster);
        }

        void AddShotCluster(IReadOnlyList<SimulationEvent> shots)
        {
            var anchor = shots[^1];
            var team = anchor.TeamId == homeTeamId ? homeTeamName : anchor.TeamId == awayTeamId ? awayTeamName : "A team";
            var count = shots.Count;
            var summary = count == 1
                ? $"A shot was recorded for {team}."
                : $"{count} shots were recorded for {team} within a {policy.ShotDeduplicationWindowSeconds}-second window.";
            candidates.Add(Create(matchId, sourceVersion, provenance, canonicalRevision, selectedTimeSeconds, throughSequence,
                anchor.Event.ClockSeconds, anchor.Event.Sequence, "shot", "fact", count == 1 ? "Shot recorded" : "Shots recorded",
                summary, policy.ShotWeight, policy, shots.Select(shot => shot.Event.EventId).ToArray(), [], []));
        }

        foreach (var insight in insights.Where(insight => insight.MatchId == matchId && insight.SourceVersion == sourceVersion &&
                     insight.ClockSeconds <= selectedTimeSeconds &&
                     insight.SelectedTimeSeconds <= selectedTimeSeconds && insight.ThroughSequence <= throughSequence &&
                     insight.Classification == "simulated" && insight.PolicyStatus == "unapproved-demo" && !insight.Publishable &&
                     insight.EvidenceGraph is { PolicyStatus: "unapproved-demo", Publishable: false }))
        {
            var graph = insight.EvidenceGraph!;
            if (graph.MatchId != matchId || graph.SourceVersion != sourceVersion || graph.Provenance != provenance ||
                graph.DetectorVersion != insight.DetectorVersion ||
                graph.CanonicalRevision != canonicalRevision || insight.CanonicalRevision != canonicalRevision ||
                graph.SelectedTimeSeconds != insight.SelectedTimeSeconds || graph.ThroughSequence != insight.ThroughSequence ||
                graph.SelectedTimeSeconds > selectedTimeSeconds || graph.ThroughSequence > throughSequence ||
                insight.EventIds.Count == 0 || insight.EventIds.Any(id => !prefixIds.Contains(id)))
                continue;
            var eventItems = prefix.Where(item => insight.EventIds.Contains(item.Event.EventId)).ToArray();
            if (eventItems.Length != insight.EventIds.Distinct().Count() || eventItems.Length == 0 ||
                insight.EventIds.Any(id => graph.Nodes.All(node => node.Kind != "event" || node.EventId != id))) continue;
            var anchor = eventItems.OrderBy(item => item.Event.Sequence).Last();
            var ids = insight.EventIds.Distinct().OrderBy(id => id).ToArray();
            var claims = graph.Nodes.Where(node => node.Kind == "claim" && node.TruthClass == "pattern")
                .Select(node => node.Id).ToArray();
            if (claims.Length == 0 || !claims.Contains(graph.ClaimNodeId, StringComparer.Ordinal) ||
                insight.FrameIds.Any(id => graph.Nodes.All(node => node.Kind != "frame" || node.FrameId != id))) continue;
            candidates.Add(Create(matchId, sourceVersion, provenance, canonicalRevision, selectedTimeSeconds, throughSequence,
                anchor.Event.ClockSeconds, anchor.Event.Sequence, "tactical-pattern", "pattern",
                "Tactical pattern candidate", "A simulated tactical sequence was detected; review its linked evidence before drawing a conclusion.",
                policy.TacticalPatternWeight, policy, ids, insight.FrameIds.Distinct().Order().ToArray(), claims));
        }

        var aged = candidates.Select(moment => moment with
        {
            Relevance = ApplyDecay(moment.Relevance, selectedTimeSeconds - moment.ClockSeconds, policy.RecencyHalfLifeSeconds)
        });
        return Array.AsReadOnly(aged
            .Where(moment => moment.Relevance >= policy.MinimumRelevance)
            .GroupBy(moment => moment.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(moment => moment.Relevance).First())
            .OrderByDescending(moment => moment.Relevance)
            .ThenBy(moment => moment.Sequence)
            .Take(policy.MaximumMoments)
            .ToArray());
    }

    private static (string? Kind, decimal Weight, string Title, string Summary) EventNarrative(
        SimulationEvent item, string homeTeamId, string homeTeamName, string awayTeamId, string awayTeamName,
        KeyMomentPolicy policy)
    {
        var team = item.TeamId == homeTeamId ? homeTeamName : item.TeamId == awayTeamId ? awayTeamName : "A team";
        return item.Event.Type switch
        {
            MatchEventType.Goal => ("goal", policy.GoalWeight, "Goal", $"A goal was recorded for {team}."),
            MatchEventType.RedCard => ("red-card", policy.RedCardWeight, "Red card", $"A red card was recorded for {team}."),
            MatchEventType.YellowCard => ("yellow-card", policy.YellowCardWeight, "Yellow card", $"A yellow card was recorded for {team}."),
            MatchEventType.Shot => ("shot", policy.ShotWeight, "Shot recorded", $"A shot was recorded for {team}."),
            _ => (null, 0, string.Empty, string.Empty)
        };
    }

    private static KeyMoment Create(string matchId, string sourceVersion, string provenance, int? canonicalRevision,
        int selectedTimeSeconds, long throughSequence, int clockSeconds, long sequence, string kind, string truthClass,
        string title, string summary, decimal baseWeight, KeyMomentPolicy policy,
        IReadOnlyList<Guid> eventIds, IReadOnlyList<string> frameIds, IReadOnlyList<string> claimNodeIds)
    {
        var sourceIdentity = string.Join(':', matchId, sourceVersion, kind,
            string.Join(',', eventIds.OrderBy(id => id).Select(id => id.ToString("N"))));
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentity))).ToLowerInvariant();
        return new(id, matchId, sourceVersion, provenance, canonicalRevision, selectedTimeSeconds, throughSequence,
            clockSeconds, sequence, kind, truthClass, title, summary, baseWeight, policy.Version,
            PolicyStatus: "unapproved-demo", Publishable: false,
            Array.AsReadOnly(eventIds.ToArray()), Array.AsReadOnly(frameIds.ToArray()), Array.AsReadOnly(claimNodeIds.ToArray()));
    }

    private static decimal ApplyDecay(decimal weight, int ageSeconds, double halfLifeSeconds) =>
        weight * (decimal)Math.Pow(0.5, ageSeconds / halfLifeSeconds);
}
