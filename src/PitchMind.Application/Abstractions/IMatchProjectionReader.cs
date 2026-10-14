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

public sealed record MatchReplaySnapshot(SelectedMatchProjection Projection, IReadOnlyList<MatchEvent> Events,
    MatchReplaySeed? Seed, MatchReplayContext? Context, MatchReplaySourceIdentity SourceIdentity,
    IReadOnlyList<SimulationEvent> ReplayEvents,
    IReadOnlyList<MatchReplayFrame> Frames);

public sealed record MatchReplaySourceIdentity(string FixtureVersion, string Provenance,
    double PitchLengthMetres, double PitchWidthMetres, MatchCompetitionRules CompetitionRules);

/// <summary>Reads only persisted projection state and events visible at the requested match time/revision.</summary>
public interface IMatchProjectionReader
{
    /// <summary>Reads every selected-time replay input from one consistent persistence snapshot.</summary>
    Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken);

    Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken);

    IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);

    Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken);

    Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken);

    IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);

    IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken);
}
