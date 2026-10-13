using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Matches;

public sealed record GetMatchAtQuery(MatchId MatchId, int SelectedSeconds, long UpToCanonicalRevision,
    string AlgorithmVersion);

public sealed record MatchAtResult(int SelectedSeconds, SelectedMatchProjection Projection,
    IReadOnlyList<MatchEvent> Events);

/// <summary>Composes a revision-pinned projection and its verified selected-time event window.</summary>
public sealed class GetMatchAtQueryService(IMatchProjectionReader reader)
{
    public async Task<MatchAtResult?> ExecuteAsync(GetMatchAtQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MatchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(query));
        if (query.UpToCanonicalRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Canonical revision must be positive.");
        if (string.IsNullOrWhiteSpace(query.AlgorithmVersion))
            throw new ArgumentException("Algorithm version is required.", nameof(query));

        var projection = await reader.GetAtAsync(query.MatchId, query.SelectedSeconds,
            query.UpToCanonicalRevision, query.AlgorithmVersion, cancellationToken);
        if (projection is null) return null;
        if (projection.CanonicalRevision > query.UpToCanonicalRevision ||
            projection.AsOfClockSeconds > query.SelectedSeconds ||
            projection.AlgorithmVersion != query.AlgorithmVersion || projection.AsOfSequence < -1)
            throw new InvalidDataException("Selected projection metadata exceeds the requested match time or revision.");

        var events = new List<MatchEvent>();
        var previousClock = 0;
        await foreach (var matchEvent in reader.ReadEventsAtAsync(projection, query.SelectedSeconds, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (matchEvent.MatchId != query.MatchId || matchEvent.Sequence != events.Count ||
                matchEvent.ClockSeconds > query.SelectedSeconds || matchEvent.ClockSeconds < previousClock)
                throw new InvalidDataException("Selected-time event window is inconsistent with its match, clock, or sequence.");
            previousClock = matchEvent.ClockSeconds;
            events.Add(matchEvent);
        }

        if (events.Count != projection.AsOfSequence + 1)
            throw new InvalidDataException("Selected-time event window does not reach the selected projection sequence.");

        return new(query.SelectedSeconds, projection, Array.AsReadOnly(events.ToArray()));
    }
}
