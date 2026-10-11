using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public sealed record SelectedMatchProjection(
    MatchState State,
    long? CanonicalRevision,
    int Revision,
    long AsOfSequence,
    int AsOfClockSeconds,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256);

/// <summary>Reads only persisted projection state and events visible at the requested match time/revision.</summary>
public interface IMatchProjectionReader
{
    Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
        CancellationToken cancellationToken);

    /// <summary>Reads a projection from one exact canonical admission revision and reducer version.</summary>
    Task<SelectedMatchProjection?> GetAtCanonicalRevisionAsync(MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion, CancellationToken cancellationToken);

    IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);
}
