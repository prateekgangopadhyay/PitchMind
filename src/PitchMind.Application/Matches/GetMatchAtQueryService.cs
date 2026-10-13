using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitchMind.Application.Matches;

public sealed record GetMatchAtQuery(MatchId MatchId, int SelectedSeconds, long UpToCanonicalRevision,
    string AlgorithmVersion);

public sealed record MatchAtResult(int SelectedSeconds, SelectedMatchProjection Projection,
    IReadOnlyList<MatchEvent> Events, MatchState ReplayedState, string ReplaySha256);

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

        var seed = await reader.ReadReplaySeedAsync(query.MatchId, cancellationToken)
            ?? throw new InvalidDataException("Match has no immutable replay seed; canonical state cannot be replayed.");
        var aggregate = MatchAggregate.FromReplaySeed(query.MatchId, seed);
        var replayEvents = new List<SimulationEvent>();
        await foreach (var item in reader.ReadReplayEventsAtAsync(projection, query.SelectedSeconds, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (item.Event.MatchId != query.MatchId || item.Event.Sequence != replayEvents.Count ||
                item.Event.ClockSeconds > query.SelectedSeconds)
                throw new InvalidDataException("Replay source event is outside the selected match-time prefix.");
            aggregate.Apply(item);
            replayEvents.Add(item);
        }
        if (replayEvents.Count != projection.AsOfSequence + 1)
            throw new InvalidDataException("Replay source prefix does not reach the selected projection sequence.");

        var replayedState = aggregate.Snapshot();
        var json = JsonSerializer.Serialize(replayedState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var replaySha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (replayedState.MatchId != projection.State.MatchId || replayedState.LastSequence != projection.AsOfSequence ||
            replayedState.ClockSeconds != projection.AsOfClockSeconds || replaySha256 != projection.StateSha256)
            throw new InvalidDataException("Deterministic replay state does not match the selected persisted projection digest.");

        return new(query.SelectedSeconds, projection, Array.AsReadOnly(events.ToArray()), replayedState, replaySha256);
    }
}
