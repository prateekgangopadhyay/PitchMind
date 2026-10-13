using PitchMind.Domain.Events;

namespace PitchMind.Domain.Simulation;

/// <summary>
/// An append-only correction record for a synthetic fixture event. The replacement
/// retains the original event identity and match-time position; only event content
/// may be corrected. Persistence/admission of correction history is an outer-layer concern.
/// </summary>
public sealed record FixtureEventCorrection(
    long Revision,
    string CorrectionId,
    Guid TargetEventId,
    string Provenance,
    SimulationEvent Replacement);

/// <summary>Resolves an immutable fixture plus correction history into ordered replay input.</summary>
public static class FixtureCorrectionHistory
{
    public static IReadOnlyList<SimulationEvent> Resolve(
        SyntheticFixture fixture,
        IEnumerable<FixtureEventCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(corrections);

        var history = corrections.ToArray();
        if (history.Any(correction => correction is null))
            throw new ArgumentException("Correction history cannot contain null records.", nameof(corrections));

        var sourceEvents = fixture.Events.ToDictionary(item => item.Event.EventId);
        var correctionIds = new HashSet<string>(StringComparer.Ordinal);
        var correctedAtRevision = new HashSet<(long Revision, Guid EventId)>();
        var effectiveEvents = new Dictionary<Guid, SimulationEvent>();

        foreach (var correction in history.OrderBy(item => item.Revision))
        {
            if (correction.Revision <= 0) throw new InvalidOperationException("Correction revision must be positive.");
            if (string.IsNullOrWhiteSpace(correction.CorrectionId) || !correctionIds.Add(correction.CorrectionId))
                throw new InvalidOperationException("Correction ID is required and must be unique.");
            if (string.IsNullOrWhiteSpace(correction.Provenance))
                throw new InvalidOperationException("Correction provenance is required.");
            if (correction.Replacement is null) throw new InvalidOperationException("Corrected event is required.");
            if (!sourceEvents.TryGetValue(correction.TargetEventId, out var original))
                throw new InvalidOperationException("Correction target does not exist in the source fixture.");
            if (!correctedAtRevision.Add((correction.Revision, correction.TargetEventId)))
                throw new InvalidOperationException("A correction revision cannot contain conflicting replacements for the same event.");

            ValidatePreservedEventIdentity(original.Event, correction.Replacement.Event);
            effectiveEvents[correction.TargetEventId] = correction.Replacement;
        }

        return Array.AsReadOnly(fixture.Events
            .Select(item => effectiveEvents.GetValueOrDefault(item.Event.EventId, item))
            .ToArray());
    }

    private static void ValidatePreservedEventIdentity(MatchEvent original, MatchEvent replacement)
    {
        if (replacement.EventId != original.EventId || replacement.MatchId != original.MatchId ||
            replacement.Sequence != original.Sequence || replacement.ClockSeconds != original.ClockSeconds ||
            replacement.Period != original.Period || replacement.OccurredAt != original.OccurredAt)
            throw new InvalidOperationException(
                "A correction must preserve the original event ID, match ID, sequence, period, clock, and occurred-at time.");
    }
}
