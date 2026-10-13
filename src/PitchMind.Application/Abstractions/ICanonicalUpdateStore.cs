using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Abstractions;

public sealed class CanonicalUpdate
{
    private CanonicalUpdate(MatchEvent @event, MatchState state, MatchState priorState, int revision,
        string algorithmVersion, string sourceSha256)
    {
        Event = @event;
        State = state;
        PriorState = priorState;
        Revision = revision;
        AlgorithmVersion = algorithmVersion;
        SourceSha256 = sourceSha256;
    }

    public MatchEvent Event { get; }
    public MatchState State { get; }
    public MatchState PriorState { get; }
    public int Revision { get; }
    public string AlgorithmVersion { get; }
    public string SourceSha256 { get; }

    public static CanonicalUpdate FromAppliedAggregate(MatchAggregate aggregate, MatchEvent appliedEvent,
        int revision, string algorithmVersion, string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(appliedEvent);
        var state = aggregate.SnapshotAfter(appliedEvent);
        var priorState = aggregate.SnapshotBefore(appliedEvent);
        if (aggregate.Id != appliedEvent.MatchId || state.LastSequence != appliedEvent.Sequence || state.ClockSeconds != appliedEvent.ClockSeconds)
            throw new InvalidOperationException("Aggregate projection does not match its applied canonical event.");
        if (priorState.MatchId != appliedEvent.MatchId.ToString() || priorState.LastSequence != appliedEvent.Sequence - 1)
            throw new InvalidOperationException("Aggregate predecessor does not match the canonical event sequence.");
        return new CanonicalUpdate(appliedEvent, state, priorState, revision, algorithmVersion, sourceSha256);
    }
}

public enum CanonicalUpdateCommitResult { Committed, AlreadyCommitted }

/// <summary>Atomically appends a canonical event, its revisioned projection, and a publication intent.</summary>
public interface ICanonicalUpdateStore
{
    Task<CanonicalUpdateCommitResult> CommitAsync(CanonicalUpdate update, CancellationToken cancellationToken);
}
