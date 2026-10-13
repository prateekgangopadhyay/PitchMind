using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Application.Abstractions;

public sealed class CanonicalUpdate
{
    private CanonicalUpdate(MatchEvent @event, SimulationEvent sourceEvent, MatchReplaySeed replaySeed,
        MatchState state, int revision, string algorithmVersion, string sourceSha256)
    {
        Event = @event;
        SourceEvent = sourceEvent;
        ReplaySeed = replaySeed;
        State = state;
        Revision = revision;
        AlgorithmVersion = algorithmVersion;
        SourceSha256 = sourceSha256;
    }

    public MatchEvent Event { get; }
    public SimulationEvent SourceEvent { get; }
    public MatchReplaySeed ReplaySeed { get; }
    public MatchState State { get; }
    public int Revision { get; }
    public string AlgorithmVersion { get; }
    public string SourceSha256 { get; }

    public static CanonicalUpdate FromAppliedAggregate(MatchAggregate aggregate, MatchEvent appliedEvent,
        int revision, string algorithmVersion, string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(appliedEvent);
        var state = aggregate.SnapshotAfter(appliedEvent);
        if (aggregate.Id != appliedEvent.MatchId || state.LastSequence != appliedEvent.Sequence || state.ClockSeconds != appliedEvent.ClockSeconds)
            throw new InvalidOperationException("Aggregate projection does not match its applied canonical event.");
        return new CanonicalUpdate(appliedEvent, aggregate.AppliedSourceEvent(appliedEvent), aggregate.ReplaySeed,
            state, revision, algorithmVersion, sourceSha256);
    }
}

public enum CanonicalUpdateCommitResult { Committed, AlreadyCommitted }

/// <summary>Atomically appends a canonical event, its revisioned projection, and a publication intent.</summary>
public interface ICanonicalUpdateStore
{
    Task<CanonicalUpdateCommitResult> CommitAsync(CanonicalUpdate update, CancellationToken cancellationToken);
}
