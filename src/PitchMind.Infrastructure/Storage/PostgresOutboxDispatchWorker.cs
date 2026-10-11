using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Polls persisted outbox intent and dispatches it through the configured publisher.</summary>
public sealed class PostgresOutboxDispatchWorker(
    PostgresOutboxDispatcher dispatcher,
    ILogger<PostgresOutboxDispatchWorker> logger,
    TimeSpan? idleDelay = null) : BackgroundService
{
    private readonly TimeSpan _idleDelay = idleDelay ?? TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchOneAsync(stoppingToken).ConfigureAwait(false);
                if (result == OutboxDispatchResult.NoMessage)
                    await Task.Delay(_idleDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Do not include payloads or connection strings in operational logs.
                logger.LogError("Outbox polling failed ({ErrorType}); retrying after {DelayMs} ms.",
                    exception.GetType().Name, _idleDelay.TotalMilliseconds);
                await Task.Delay(_idleDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
