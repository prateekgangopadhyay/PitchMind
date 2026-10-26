using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

public sealed record CandidateSimulationStatus(string? MatchId, bool IsStreaming, long? CurrentSequence,
    decimal? CurrentClockValue, int? Period, string? SourceSha256,
    string SourceStatus = "candidate_not_canonical", string CoordinateStatus = "unverified",
    string PlayerMetricsStatus = "unvalidated");

public sealed class CandidateFeedSourceCatalog(IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CandidateFeedDocument? _cached;
    public async Task<CandidateFeedDocument> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null) return _cached;
            var relative = configuration["CandidateFeed:Path"] ?? "data/simulations/manu-liv-match-dataset.json";
            if (Path.IsPathRooted(relative)) throw new InvalidOperationException("CandidateFeed:Path must be repository-relative.");
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            string? path = null;
            for (var i = 0; directory is not null && i < 8; i++, directory = directory.Parent)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.FullName, relative));
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            if (path is null) throw new FileNotFoundException("Candidate simulation feed was not found.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var loaded = await ManuLivCandidateFeedReader.ReadAsync(input, cancellationToken).ConfigureAwait(false);
            var id = loaded.Events[0].SourceMatchId;
            if (loaded.Events.Any(item => item.SourceMatchId != id))
                throw new InvalidDataException("Candidate source contains multiple matches; replay is blocked.");
            _cached = loaded;
            return loaded;
        }
        finally { _gate.Release(); }
    }
}

/// <summary>LOCAL simulation control; its source cursor advances after Kafka acknowledgement.</summary>
public sealed class LocalCandidateSimulationService(CandidateFeedSourceCatalog catalog, ICandidateEventPublisher publisher,
    IConfiguration configuration, ILogger<LocalCandidateSimulationService> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalCandidateFeedReplaySession? _session;
    private bool _streaming;

    public async Task<CandidateSimulationStatus> GetStatusAsync(CandidateFeedPersistence persistence, CancellationToken token)
    {
        var source = await catalog.GetAsync(token).ConfigureAwait(false);
        var cursor = await persistence.ReadLatestCursorAsync(token).ConfigureAwait(false);
        var match = source.Events[0].SourceMatchId;
        return cursor is not null && cursor.SourceMatchId == match && cursor.SourceSha256 == source.SourceSha256
            ? new(match, _streaming, cursor.SourceSequence, cursor.RawClockValue, cursor.RawPeriod, source.SourceSha256)
            : new(match, _streaming, null, null, null, source.SourceSha256);
    }

    public async Task<CandidateSimulationStatus> StartAsync(CandidateFeedPersistence persistence, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_session?.State == CandidateFeedReplaySessionState.Paused)
            {
                await _session.ResumeAsync().ConfigureAwait(false); _streaming = true;
                return await GetStatusAsync(persistence, token).ConfigureAwait(false);
            }
            if (_session is null || _session.State is CandidateFeedReplaySessionState.Completed or CandidateFeedReplaySessionState.Canceled or CandidateFeedReplaySessionState.Faulted)
            {
                if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false);
                await publisher.EnsureReadyAsync(token).ConfigureAwait(false);
                var source = await catalog.GetAsync(token).ConfigureAwait(false);
                // The API container runs as a non-root user and /app is an image-owned,
                // read-only application directory. Replay checkpoints are ephemeral
                // producer state, so keep them in the runtime's writable temp directory.
                var checkpoint = new FileCandidateFeedReplayCheckpointStore(Path.Combine(
                    ResolveTempDirectory(), "pitchmind", "candidate-feed", source.SourceSha256,
                    Guid.NewGuid().ToString("N")));
                var rate = configuration.GetValue("CandidateFeed:RecordsPerSecond", 10);
                if (rate is < 1 or > LocalCandidateFeedReplayProducer.MaximumRecordsPerSecond)
                    throw new InvalidOperationException("CandidateFeed:RecordsPerSecond is outside the supported range.");
                _session = await LocalCandidateFeedReplaySession.CreateAsync(source, checkpoint,
                    async (envelope, ct) => await publisher.PublishAsync(envelope, ct).ConfigureAwait(false), rate, token).ConfigureAwait(false);
                var current = _session; _streaming = true;
                _ = ObserveAsync(current, current.Start());
            }
            return await GetStatusAsync(persistence, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<CandidateSimulationStatus> PauseAsync(CandidateFeedPersistence persistence, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_session?.State == CandidateFeedReplaySessionState.Running)
                await _session.PauseAsync(token).ConfigureAwait(false);
            _streaming = false;
            return await GetStatusAsync(persistence, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task ObserveAsync(LocalCandidateFeedReplaySession session, Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogError("Candidate replay failed ({ExceptionType}).", ex.GetType().Name); }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { if (ReferenceEquals(_session, session)) _streaming = false; }
            finally { _gate.Release(); }
        }
    }
    public async Task ResetAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
            _streaming = false;
        }
        finally { _gate.Release(); }
    }

    private static string ResolveTempDirectory()
    {
        var temp = Path.GetTempPath();
        if (OperatingSystem.IsMacOS())
        {
            if (temp.StartsWith("/var/", StringComparison.Ordinal))
                temp = "/private" + temp;
            else if (temp.StartsWith("/tmp/", StringComparison.Ordinal))
                temp = "/private" + temp;
        }
        return temp;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
