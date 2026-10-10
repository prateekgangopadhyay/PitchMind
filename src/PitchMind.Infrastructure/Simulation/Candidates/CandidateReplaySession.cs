using System.Runtime.CompilerServices;

namespace PitchMind.Infrastructure.Simulation.Candidates;

/// <summary>
/// Controls one in-process replay of a candidate feed. Session state and audit entries are
/// operational only; they do not represent or persist canonical football state.
/// </summary>
public sealed class CandidateReplaySession
{
    private readonly CandidateSourceFeed _feed;
    private readonly long _initialSourceSequence;
    private readonly CandidateReplayOptions _options;
    private readonly object _sync = new();
    private readonly List<CandidateReplayAuditEntry> _auditTrail = [];
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource _resumeSignal = CreateSignaledSignal();
    private CandidateReplaySessionState _state = CandidateReplaySessionState.Ready;
    private long? _lastEmittedSourceSequence;
    private int _enumerationStarted;

    public CandidateReplaySession(
        CandidateSourceFeed feed,
        long initialSourceSequence,
        CandidateReplayOptions options)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        if (initialSourceSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(initialSourceSequence));

        _initialSourceSequence = initialSourceSequence;
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CandidateReplaySessionState State
    {
        get { lock (_sync) return _state; }
    }

    public long? LastEmittedSourceSequence
    {
        get { lock (_sync) return _lastEmittedSourceSequence; }
    }

    /// <summary>
    /// Returns an immutable snapshot of the source-hash and cursor transitions observed in this process.
    /// </summary>
    public IReadOnlyList<CandidateReplayAuditEntry> AuditTrail
    {
        get { lock (_sync) return _auditTrail.ToArray(); }
    }

    /// <summary>Pauses before the next source envelope is emitted.</summary>
    public void Pause()
    {
        lock (_sync)
        {
            if (_state != CandidateReplaySessionState.Running)
                throw new InvalidOperationException("Only a running candidate replay can be paused.");

            _state = CandidateReplaySessionState.Paused;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Record(CandidateReplayTransition.Paused);
        }
    }

    /// <summary>Resumes a paused replay from the next source sequence.</summary>
    public void Resume()
    {
        lock (_sync)
        {
            if (_state != CandidateReplaySessionState.Paused)
                throw new InvalidOperationException("Only a paused candidate replay can be resumed.");

            _state = CandidateReplaySessionState.Running;
            Record(CandidateReplayTransition.Resumed);
            _resumeSignal.TrySetResult();
        }
    }

    /// <summary>Stops the current replay. A stopped session is terminal and cannot be resumed.</summary>
    public void Cancel()
    {
        lock (_sync)
        {
            if (_state is CandidateReplaySessionState.Completed or CandidateReplaySessionState.Cancelled)
                return;

            _state = CandidateReplaySessionState.Cancelled;
            Record(CandidateReplayTransition.Cancelled);
            _resumeSignal.TrySetResult();
        }
        _stop.Cancel();
    }

    /// <summary>
    /// Replays source envelopes in order. A session can be enumerated once; pause and resume
    /// preserve the cursor only while this process and enumerator remain alive.
    /// </summary>
    public async IAsyncEnumerable<CandidateSourceEnvelope> ReplayAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerationStarted, 1) != 0)
            throw new InvalidOperationException("A candidate replay session can be enumerated only once.");

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var completed = false;
        try
        {
            lock (_sync)
            {
                if (_state != CandidateReplaySessionState.Ready)
                    throw new InvalidOperationException("A candidate replay session must be ready when enumeration starts.");

                _state = CandidateReplaySessionState.Running;
                Record(CandidateReplayTransition.Started);
            }

            var nextSourceSequence = _initialSourceSequence;
            await foreach (var envelope in _feed.ReplayFromSourceSequenceAsync(
                _initialSourceSequence,
                _options,
                linkedCancellation.Token).ConfigureAwait(false))
            {
                if (!await RecordEmissionWhenRunningAsync(envelope, linkedCancellation.Token).ConfigureAwait(false))
                    yield break;
                nextSourceSequence = envelope.SourceSequence + 1;
                yield return envelope;
            }

            lock (_sync)
            {
                if (_state != CandidateReplaySessionState.Cancelled)
                {
                    _state = CandidateReplaySessionState.Completed;
                    Record(CandidateReplayTransition.Completed, nextSourceSequence: nextSourceSequence);
                    completed = true;
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                if (!completed && _state is CandidateReplaySessionState.Running or CandidateReplaySessionState.Paused)
                {
                    _state = CandidateReplaySessionState.Cancelled;
                    Record(CandidateReplayTransition.Cancelled);
                    _resumeSignal.TrySetResult();
                }
            }
        }
    }

    private async Task WaitUntilRunningAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task resumeTask;
            lock (_sync)
            {
                if (_state != CandidateReplaySessionState.Paused)
                    return;
                resumeTask = _resumeSignal.Task;
            }

            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> RecordEmissionWhenRunningAsync(
        CandidateSourceEnvelope envelope,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitUntilRunningAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (_state == CandidateReplaySessionState.Cancelled)
                    return false;
                if (_state == CandidateReplaySessionState.Paused)
                    continue;

                _lastEmittedSourceSequence = envelope.SourceSequence;
                Record(CandidateReplayTransition.Emitted, envelope.EventId);
                return true;
            }
        }
    }

    private void Record(CandidateReplayTransition transition, string? eventId = null, long? nextSourceSequence = null)
    {
        _auditTrail.Add(new CandidateReplayAuditEntry(
            _auditTrail.Count + 1,
            transition,
            _feed.SourceSha256,
            eventId,
            _lastEmittedSourceSequence,
            nextSourceSequence ?? (_lastEmittedSourceSequence is null ? _initialSourceSequence : _lastEmittedSourceSequence.Value + 1)));
    }

    private static TaskCompletionSource CreateSignaledSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}

public enum CandidateReplaySessionState
{
    Ready,
    Running,
    Paused,
    Completed,
    Cancelled
}

public enum CandidateReplayTransition
{
    Started,
    Emitted,
    Paused,
    Resumed,
    Cancelled,
    Completed
}

/// <summary>An in-memory operational audit record, not a durable checkpoint.</summary>
public sealed record CandidateReplayAuditEntry(
    long Ordinal,
    CandidateReplayTransition Transition,
    string SourceSha256,
    string? EventId,
    long? LastEmittedSourceSequence,
    long NextSourceSequence);
