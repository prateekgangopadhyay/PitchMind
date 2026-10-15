using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public sealed record PersistedTrackingFrame(
    string FrameId,
    long Sequence,
    long CanonicalRevision,
    int Period,
    int ClockSeconds,
    string CoordinateVersion,
    string QualityJson,
    string SourceSha256,
    string PayloadJson);

public sealed record PersistedTrackingFramePage(
    IReadOnlyList<PersistedTrackingFrame> Frames,
    long? NextAfterSequence,
    string? NextAfterFrameId,
    bool HasMore);

/// <summary>Reads only persisted, source-bound tracking frames visible at a selected time and canonical revision.</summary>
public interface IMatchTrackingReader
{
    Task<PersistedTrackingFramePage> ReadAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        long? afterSequence, string? afterFrameId, int limit, CancellationToken cancellationToken);
}
