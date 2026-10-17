using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using PitchMind.Application.Abstractions;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Match;

namespace PitchMind.Api;

public interface ISimulationStreamer
{
    Guid ActiveMatchId { get; }
    SelectedMatchProjection? GetProjection(Guid matchId);
    Task<SimulationStreamStatus> StartAsync(Guid matchId, CancellationToken cancellationToken);
    Task<SimulationStreamStatus> PauseAsync(CancellationToken cancellationToken);
    Task<SimulationStreamStatus> StepAsync(CancellationToken cancellationToken);
    SimulationStreamStatus GetStatus();
}

public sealed record SimulationStreamStatus(
    Guid MatchId,
    bool IsStreaming,
    int CurrentClockSeconds,
    long CurrentSequence,
    long CanonicalRevision,
    int EventsCount,
    int HomeScore,
    int AwayScore,
    string? LeadingInsight);

public sealed class SimulationStreamerService : ISimulationStreamer, IDisposable
{
    private static readonly Guid DefaultSimulationMatchGuid = new("e0000000-0000-0000-0000-000000000001");
    private readonly IHubContext<MatchUpdatesHub, IMatchUpdateClient> _hub;
    private readonly IMatchProjectionCache _cache;
    private readonly ILogger<SimulationStreamerService> _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _streamCts;
    private Task? _streamingTask;
    private bool _isStreaming;
    private int _currentClockSeconds = 4044; // 67:24 matching simulation fixture default
    private long _currentSequence = 120;
    private long _canonicalRevision = 1;
    private int _homeScore = 2;
    private int _awayScore = 1;
    private string? _leadingInsight = "Arsenal's high press is forcing turnovers in Zone 14.";

    private readonly ConcurrentDictionary<Guid, SelectedMatchProjection> _cachedProjections = new();

    public Guid ActiveMatchId => DefaultSimulationMatchGuid;

    public SimulationStreamerService(
        IHubContext<MatchUpdatesHub, IMatchUpdateClient> hub,
        IMatchProjectionCache cache,
        ILogger<SimulationStreamerService> logger)
    {
        _hub = hub;
        _cache = cache;
        _logger = logger;
        InitializeDefaultProjection();
    }

    public SelectedMatchProjection? GetProjection(Guid matchId)
    {
        if (matchId == DefaultSimulationMatchGuid || _cachedProjections.TryGetValue(matchId, out _))
        {
            return _cachedProjections.GetOrAdd(matchId, id => CreateProjection(id, _currentClockSeconds, _canonicalRevision, _currentSequence));
        }
        return null;
    }

    public SimulationStreamStatus GetStatus()
    {
        lock (_gate)
        {
            return new SimulationStreamStatus(
                DefaultSimulationMatchGuid,
                _isStreaming,
                _currentClockSeconds,
                _currentSequence,
                _canonicalRevision,
                (int)_currentSequence,
                _homeScore,
                _awayScore,
                _leadingInsight);
        }
    }

    public Task<SimulationStreamStatus> StartAsync(Guid matchId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_isStreaming)
                return Task.FromResult(GetStatus());

            _isStreaming = true;
            _streamCts?.Dispose();
            _streamCts = new CancellationTokenSource();
            var token = _streamCts.Token;

            _streamingTask = Task.Run(async () =>
            {
                _logger.LogInformation("DEV simulation streaming started for match {MatchId}", matchId);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        await StepInternalAsync(matchId, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Simulation stream iteration failed");
                    }
                }
            }, token);

            return Task.FromResult(GetStatus());
        }
    }

    public Task<SimulationStreamStatus> PauseAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _isStreaming = false;
            _streamCts?.Cancel();
            return Task.FromResult(GetStatus());
        }
    }

    public async Task<SimulationStreamStatus> StepAsync(CancellationToken cancellationToken)
    {
        return await StepInternalAsync(DefaultSimulationMatchGuid, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SimulationStreamStatus> StepInternalAsync(Guid matchId, CancellationToken cancellationToken)
    {
        SimulationStreamStatus status;
        SelectedMatchProjection projection;
        lock (_gate)
        {
            _currentClockSeconds = Math.Min(5400, _currentClockSeconds + 15);
            _currentSequence++;
            _canonicalRevision++;
            projection = CreateProjection(matchId, _currentClockSeconds, _canonicalRevision, _currentSequence);
            _cachedProjections[matchId] = projection;
            status = GetStatus();
        }

        // Store projection in cache
        var cacheKey = $"match-projection:{matchId:D}:{projection.CanonicalRevision}:{_currentClockSeconds}:canonical-v1";
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(projection));
        await _cache.SetAsync(cacheKey, payloadBytes, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);

        // Broadcast realtime cursor wake-up over SignalR
        var cursor = new RealtimeCursorV1(
            "matchUpdated",
            matchId.ToString("D"),
            projection.CanonicalRevision,
            projection.AsOfSequence,
            DateTimeOffset.UtcNow,
            1);

        try
        {
            await _hub.Clients.Group(MatchUpdatesHub.GroupName(matchId))
                .MatchUpdated(cursor)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Realtime cursor broadcast skipped (no active group subscribers)");
        }

        return status;
    }

    private void InitializeDefaultProjection()
    {
        var proj = CreateProjection(DefaultSimulationMatchGuid, _currentClockSeconds, _canonicalRevision, _currentSequence);
        _cachedProjections[DefaultSimulationMatchGuid] = proj;
    }

    private static SelectedMatchProjection CreateProjection(Guid matchId, int clockSeconds, long revision, long sequence)
    {
        var state = new MatchState(
            matchId.ToString("D"),
            clockSeconds,
            clockSeconds < 2700 ? 1 : 2,
            clockSeconds < 2700 ? MatchPhase.FirstHalf : MatchPhase.SecondHalf,
            2,
            1,
            "home",
            11,
            11,
            Array.Empty<MatchPlayerState>(),
            sequence,
            "simulation");

        return new SelectedMatchProjection(
            state,
            revision,
            1,
            sequence,
            clockSeconds,
            "sim-v1",
            "sim-sha",
            "state-sha");
    }

    public void Dispose()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
    }
}
