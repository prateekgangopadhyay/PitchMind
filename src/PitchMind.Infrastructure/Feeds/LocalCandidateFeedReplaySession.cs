namespace PitchMind.Infrastructure.Feeds;

public enum CandidateFeedReplaySessionState
{
    Created,
    Running,
    Pausing,
    Paused,
    Completed,
    Canceled,
    Faulted
}

/// <summary>
/// Coordinates one local raw-feed replay. A record becomes acknowledged only when the
/// consumer returns successfully and the source-bound cursor is durably saved afterward.
/// Consumer side effects must be idempotent because a crash between handling and checkpoint
/// replacement can replay the same source record.
/// </summary>
public sealed class LocalCandidateFeedReplaySession : IAsyncDisposable
{
    private readonly CandidateFeedDocument _document;
    private readonly ICandidateFeedReplayCheckpointStore _checkpointStore;
    private readonly Func<CandidateReplayEnvelope, CancellationToken, ValueTask> _consumer;
    private readonly int _recordsPerSecond;
    private IAsyncDisposable? _sessionLease;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private TaskCompletionSource _resumeSignal = CompletedSignal();
    private TaskCompletionSource? _pauseSignal;
    private CandidateFeedReplayCursor? _cursor;
    private Task? _runTask;
    private CandidateFeedReplaySessionState _state = CandidateFeedReplaySessionState.Created;

    private LocalCandidateFeedReplaySession(
        CandidateFeedDocument document,
        ICandidateFeedReplayCheckpointStore checkpointStore,
        Func<CandidateReplayEnvelope, CancellationToken, ValueTask> consumer,
        CandidateFeedReplayCursor? cursor,
        IAsyncDisposable? sessionLease,
        int recordsPerSecond)
    {
        _document = document;
        _checkpointStore = checkpointStore;
        _consumer = consumer;
        _cursor = cursor;
        _sessionLease = sessionLease;
        _recordsPerSecond = recordsPerSecond;
    }

    public CandidateFeedReplaySessionState State
    {
        get { lock (_sync) return _state; }
    }

    public CandidateFeedReplayCursor? LastAcknowledgedCursor
    {
        get { lock (_sync) return _cursor; }
    }

    public static async Task<LocalCandidateFeedReplaySession> CreateAsync(
        CandidateFeedDocument document,
        ICandidateFeedReplayCheckpointStore checkpointStore,
        Func<CandidateReplayEnvelope, CancellationToken, ValueTask> consumer,
        int recordsPerSecond = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(consumer);
        if (recordsPerSecond is < 1 or > LocalCandidateFeedReplayProducer.MaximumRecordsPerSecond)
            throw new ArgumentOutOfRangeException(nameof(recordsPerSecond));

        IAsyncDisposable? sessionLease = null;
        try
        {
            if (checkpointStore is ICandidateFeedReplaySessionLeaseProvider leaseProvider)
                sessionLease = await leaseProvider.AcquireSessionLeaseAsync(cancellationToken).ConfigureAwait(false);
            var cursor = await checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return new LocalCandidateFeedReplaySession(document, checkpointStore, consumer, cursor,
                sessionLease, recordsPerSecond);
        }
        catch
        {
            if (sessionLease is not null) await sessionLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Starts the session and returns its completion task.</summary>
    public Task Start()
    {
        lock (_sync)
        {
            if (_state != CandidateFeedReplaySessionState.Created)
                throw new InvalidOperationException("Candidate replay session can only be started once.");
            _state = CandidateFeedReplaySessionState.Running;
            _runTask = RunAsync();
            return _runTask;
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (_sync)
        {
            if (_state == CandidateFeedReplaySessionState.Paused) return;
            if (_state == CandidateFeedReplaySessionState.Running)
            {
                _state = CandidateFeedReplaySessionState.Pausing;
                _pauseSignal = NewSignal();
                _resumeSignal = NewSignal();
            }
            else if (_state != CandidateFeedReplaySessionState.Pausing)
            {
                throw new InvalidOperationException($"Cannot pause a replay session in state {_state}.");
            }
            wait = _pauseSignal!.Task;
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (_state is CandidateFeedReplaySessionState.Completed or CandidateFeedReplaySessionState.Canceled or
                CandidateFeedReplaySessionState.Faulted)
                throw new InvalidOperationException($"Replay session ended in state {_state} before it could pause.");
        }
    }

    public Task ResumeAsync()
    {
        lock (_sync)
        {
            if (_state != CandidateFeedReplaySessionState.Paused)
                throw new InvalidOperationException($"Cannot resume a replay session in state {_state}.");
            _state = CandidateFeedReplaySessionState.Running;
            _resumeSignal.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public async Task CancelAsync()
    {
        Task? runTask;
        lock (_sync)
        {
            if (_state is CandidateFeedReplaySessionState.Completed or CandidateFeedReplaySessionState.Canceled or
                CandidateFeedReplaySessionState.Faulted or CandidateFeedReplaySessionState.Created)
            {
                if (_state == CandidateFeedReplaySessionState.Created)
                    _state = CandidateFeedReplaySessionState.Canceled;
                return;
            }
            _shutdown.Cancel();
            _resumeSignal.TrySetResult();
            runTask = _runTask;
        }

        if (runTask is not null)
        {
            try { await runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync().ConfigureAwait(false);
        var lease = Interlocked.Exchange(ref _sessionLease, null);
        if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var envelope in LocalCandidateFeedReplayProducer.ReplayAsync(
                               _document, _cursor, _recordsPerSecond, _shutdown.Token).ConfigureAwait(false))
            {
                await WaitAtPauseBoundaryAsync(_shutdown.Token).ConfigureAwait(false);
                await _consumer(envelope, _shutdown.Token).ConfigureAwait(false);
                await _checkpointStore.SaveAsync(envelope.ResumeCursor, _shutdown.Token).ConfigureAwait(false);
                lock (_sync) _cursor = envelope.ResumeCursor;
                await WaitAtPauseBoundaryAsync(_shutdown.Token).ConfigureAwait(false);
            }

            SetTerminalState(CandidateFeedReplaySessionState.Completed);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            SetTerminalState(CandidateFeedReplaySessionState.Canceled);
            throw;
        }
        catch
        {
            SetTerminalState(CandidateFeedReplaySessionState.Faulted);
            throw;
        }
    }

    private async Task WaitAtPauseBoundaryAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_sync)
        {
            if (_state == CandidateFeedReplaySessionState.Pausing)
            {
                _state = CandidateFeedReplaySessionState.Paused;
                _pauseSignal!.TrySetResult();
            }

            if (_state != CandidateFeedReplaySessionState.Paused) return;
            wait = _resumeSignal.Task;
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetTerminalState(CandidateFeedReplaySessionState terminalState)
    {
        lock (_sync)
        {
            _state = terminalState;
            _pauseSignal?.TrySetResult();
            _resumeSignal.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult();
        return signal;
    }
}
