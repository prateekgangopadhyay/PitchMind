using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Api;

internal sealed class UnavailableCanonicalReadAdapters : IMatchProjectionReader, IMatchInsightReader, IMatchTrackingReader
{
    public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        long afterSequence, int limit, CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, int maximumRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, int maximumRevision, int selectedSeconds,
        long throughSequence, CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();

    public Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
        int maximumRevision, int selectedSeconds, long throughSequence, CancellationToken cancellationToken) =>
        throw new CanonicalReadUnavailableException();

    public Task<PersistedTrackingFramePage> ReadAtAsync(SelectedMatchProjection projection, int selectedSeconds, int limit,
        CancellationToken cancellationToken) => throw new CanonicalReadUnavailableException();
}
