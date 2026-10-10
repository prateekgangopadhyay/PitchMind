using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public sealed record SelectedMatchProjection(
    MatchState State,
    int Revision,
    long AsOfSequence,
    int AsOfClockSeconds,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256);

public sealed record PersistedMatchEventPage(
    IReadOnlyList<MatchEvent> Events,
    long? NextAfterSequence,
    bool HasMore);

public sealed record PersistedTrackingFrame(
    string FrameId,
    long Sequence,
    int Period,
    int ClockSeconds,
    string CoordinateVersion,
    string QualityJson,
    string SourceSha256,
    string PayloadJson);

public sealed record PersistedTrackingFramePage(IReadOnlyList<PersistedTrackingFrame> Frames, bool HasMore);

/// <summary>Reads only persisted projection state and events visible at the requested match time/revision.</summary>
public interface IMatchProjectionReader
{
    Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
        CancellationToken cancellationToken);

    Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
        CancellationToken cancellationToken);

    Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        long afterSequence, int limit, CancellationToken cancellationToken);

    IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);
}

/// <summary>Reads immutable tracking frames visible at the selected time and projection watermark.</summary>
public interface IMatchTrackingReader
{
    Task<PersistedTrackingFramePage> ReadAtAsync(PersistedMatchProjection projection, int selectedSeconds, int limit,
        CancellationToken cancellationToken);
}
