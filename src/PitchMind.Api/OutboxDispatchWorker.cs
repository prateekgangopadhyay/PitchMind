using Microsoft.Extensions.Hosting;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Api;

/// <summary>Runs the durable PostgreSQL outbox dispatcher for the local SignalR profile.</summary>
public sealed class OutboxDispatchWorker(
    Func<CancellationToken, Task<OutboxDispatchResult>> dispatchOne,
    ILogger<OutboxDispatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DispatchDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxDispatchResult result;
            try
            {
                result = await dispatchOne(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Avoid logging payloads or provider error details, which may contain connection metadata.
                logger.LogWarning("Outbox dispatch attempt failed ({ExceptionType}); retrying", exception.GetType().Name);
                await Task.Delay(FailureDelay, stoppingToken).ConfigureAwait(false);
                continue;
            }

            var delay = result == OutboxDispatchResult.NoMessage ? IdleDelay : DispatchDelay;
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
