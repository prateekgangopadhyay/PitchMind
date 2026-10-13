using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Application.Abstractions;

public sealed record SelectedMatchProjection(
    MatchState State,
    long CanonicalRevision,
    int ProjectionRevision,
    long AsOfSequence,
    int AsOfClockSeconds,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256);

/// <summary>Reads only persisted projection state and events visible at the requested match time/revision.</summary>
public interface IMatchProjectionReader
{
    Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken);

    IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);

    Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken);

    IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);
}
