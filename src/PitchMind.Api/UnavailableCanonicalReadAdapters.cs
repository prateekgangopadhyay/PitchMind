using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Api;

internal sealed class UnavailableCanonicalReadAdapters : IMatchProjectionReader, IMatchInsightReader
{
    public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
        long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        long afterSequence, int limit, CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, long maximumCanonicalRevision,
        int maximumProjectionRevision, int selectedSeconds, long throughSequence, long? afterSequence,
        string? afterInsightId, int limit, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, long maximumCanonicalRevision,
        int maximumProjectionRevision, int selectedSeconds, long throughSequence, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
        long maximumCanonicalRevision, int maximumProjectionRevision, int selectedSeconds, long throughSequence,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();
}
