using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

public sealed class CandidateOutboxDispatchWorker(CandidateFeedPersistence persistence, IMatchProjectionCache cache,
    IHubContext<MatchUpdatesHub, IMatchUpdateClient> hub, ILogger<CandidateOutboxDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CandidateFeedOutboxWorkItem? item = null;
            try
            {
                item = await persistence.ClaimOutboxAsync(stoppingToken).ConfigureAwait(false);
                if (item is null) { await Task.Delay(250, stoppingToken).ConfigureAwait(false); continue; }
                var cursor = item.Cursor;
                if (!Guid.TryParse(cursor.SourceMatchId, out var matchId) || matchId == Guid.Empty)
                    throw new InvalidDataException("Candidate source match ID is not a realtime UUID.");
                var signal = new CandidateRealtimeCursor(cursor.SourceMatchId, cursor.SourceSha256,
                    cursor.SourceSequence, cursor.RawClockValue, cursor.RawPeriod);
                var key = $"candidate-live:v1:{cursor.SourceSha256}:{cursor.SourceMatchId}";
                var eventWrite = persistence.PersistCandidateEventAsync(item.Message, stoppingToken);
                var cacheWrite = cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(signal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)), TimeSpan.FromMinutes(5), stoppingToken);
                await Task.WhenAll(eventWrite, cacheWrite).ConfigureAwait(false);
                try
                {
                    await hub.Clients.Group(MatchUpdatesHub.CandidateGroupName(matchId, cursor.SourceSha256))
                        .CandidateMatchUpdated(signal).ConfigureAwait(false);
                }
                catch (Exception signalREx)
                {
                    logger.LogWarning(signalREx, "Failed to broadcast candidate match update over SignalR; continuing acknowledgment");
                }
                await persistence.AcknowledgeOutboxAsync(item, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (item is not null) await ReleaseAsync(item).ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                if (item is not null) await ReleaseAsync(item).ConfigureAwait(false);
                logger.LogWarning("Candidate outbox fan-out failed ({ExceptionType}); retrying.", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }
    private async Task ReleaseAsync(CandidateFeedOutboxWorkItem item)
    {
        try { await persistence.ReleaseOutboxAsync(item, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { logger.LogWarning("Candidate outbox lease release deferred ({ExceptionType}).", ex.GetType().Name); }
    }
}
